using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Formulas;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Meshmakers.Octo.Runtime.Engine.Blueprints;
using Meshmakers.Octo.Runtime.Engine.Exchange;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// Refuses a change to an archive with a table that the platform cannot apply (AB#7872). The definition of an archive is a
/// plain entity, so the generic entity API, <c>ImportRt</c> and a blueprint update can write any attribute of it; the
/// table does not follow, and a rollup then stops with only a log line telling. The check runs where those ways in
/// write: <see cref="Repositories.RuntimeRepositoryBase" />'s public ApplyChanges and the import.
/// </summary>
/// <remarks>
/// <para>
/// The stream data stores do not pass here. They write through <c>UpdateOneRtEntityById</c>: the lifecycle of a computed
/// column, the watermark, the status. Neither do CK model migrations, which change definitions on purpose.
/// </para>
/// <para>
/// Compared is what the write would store (<see cref="BlueprintEntityComparer" />, the comparison of the blueprint
/// preview), not what the input literally says, so a re-apply of an unchanged definition passes whatever form it has.
/// The columns of a rollup are derived and not compared, and its platform columns (AB#7727) are no part of its definition.
/// Guarded are <see cref="CkArchiveStatus.Activated" /> and a <see cref="CkArchiveStatus.Disabled" /> archive that was
/// activated before: enabling it again meets the same table, so disable, change, enable would be a way around the guard.
/// Seeds import their archives as Disabled, so a disabled archive that was never activated has no table and its seed
/// stays free to change. <c>FirstActivatedAt</c> tells them apart; a rollup activated before that attribute existed
/// shows it by its watermark. A <see cref="CkArchiveStatus.Failed" /> archive stays editable: removing an offending
/// column is the way out of that state (the archive stops ingesting on a Hidden attribute, for example).
/// </para>
/// </remarks>
internal static class ArchiveDefinitionGuard
{
    private const string ModelName = "System.StreamData";
    private const string ArchiveTypeName = "Archive";
    private const string StatusAttribute = "Status";
    private const string FirstActivatedAtAttribute = "FirstActivatedAt";
    private const string LastAggregatedBucketEndAttribute = "LastAggregatedBucketEnd";
    private const string TargetCkTypeIdAttribute = "TargetCkTypeId";
    private const string ColumnsAttribute = "Columns";
    private const string AggregationsAttribute = "Aggregations";
    private const string SourcesAttribute = "Sources";
    private const string SourceArchiveRtIdAttribute = "SourceArchiveRtId";
    private const string PathMember = "Path";
    private const string NameMember = "Name";
    private const string ResultTypeMember = "ResultType";
    private const string FormulaMember = "Formula";
    private const string IndexedMember = "Indexed";

    private static readonly string[] FrozenRollupAttributes =
        [TargetCkTypeIdAttribute, AggregationsAttribute, "BucketSizeMs", "BucketAlignment", "ReferenceTimeZone", "DayStartOffsetMs"];

    private static readonly string[] FrozenBaseAttributes = [TargetCkTypeIdAttribute];

    /// <summary>True when <paramref name="ckTypeId" /> is an archive type of the tenant.</summary>
    public static bool IsArchive(ICkCacheService cacheService, string tenantId, RtCkId<CkTypeId> ckTypeId,
        out CkTypeGraph? ckTypeGraph)
    {
        ckTypeGraph = null;
        if (!cacheService.TryGetRtCkType(tenantId, ckTypeId, out var graph) || graph == null)
        {
            return false;
        }

        ckTypeGraph = graph;
        return graph.GetBaseTypes(includeSelf: true)
            .Any(t => string.Equals(t.ModelId.Name, ModelName, StringComparison.OrdinalIgnoreCase)
                      && string.Equals(t.ElementId.Name, ArchiveTypeName, StringComparison.Ordinal));
    }

    /// <summary>The import replaces the whole entity, so an attribute the file omits is a change too.</summary>
    /// <exception cref="ArchiveSchemaImmutableException">A column or the target type of an archive would change.</exception>
    /// <exception cref="RollupSchemaImmutableException">A frozen attribute of a rollup would change.</exception>
    public static void EnsureUnchanged(ICkCacheService cacheService, string tenantId, CkTypeGraph ckType,
        RtEntity stored, RtEntityTcDto incoming)
    {
        Ensure(cacheService, tenantId, ckType, stored, incoming, partial: false);
    }

    /// <summary>
    /// An update carries only the attributes it writes, a replace the whole entity.
    /// </summary>
    /// <exception cref="ArchiveSchemaImmutableException">A column or the target type of an archive would change.</exception>
    /// <exception cref="RollupSchemaImmutableException">A frozen attribute of a rollup would change.</exception>
    public static void EnsureUnchanged(ICkCacheService cacheService, string tenantId, CkTypeGraph ckType,
        RtEntity stored, IEntityUpdateInfo<RtEntity> update)
    {
        var incoming = new RtEntityTcDto { RtId = stored.RtId, CkTypeId = update.CkTypeId };
        var partial = update.ModOption == EntityModOptions.Update;
        foreach (var attribute in ckType.AllAttributes.Values)
        {
            if (update.RtEntity!.Attributes.TryGetValue(attribute.AttributeName, out var value))
            {
                incoming.Attributes.Add(new RtAttributeTcDto
                {
                    Id = attribute.CkAttributeId.ToRtCkId(),
                    Value = ImportRtModelCommand.ToTransportValue(cacheService, tenantId, value)
                });
            }
        }

        foreach (var itemUpdate in update.RecordArrayItemUpdates ?? [])
        {
            // One item of a frozen list is a change of that list.
            ThrowIfFrozen(ckType, stored, itemUpdate.AttributeName);
        }

        Ensure(cacheService, tenantId, ckType, stored, incoming, partial);
    }

    /// <summary>
    /// AB#7997: takes the columns the seed adds to a guarded archive out of <paramref name="incoming" /> and returns them,
    /// so the import writes the definition without them and the archive lifecycle adds each with its storage afterwards.
    /// Run after <see cref="EnsureUnchanged(ICkCacheService, string, CkTypeGraph, RtEntity, RtEntityTcDto)" />, which
    /// already refused removed and retyped columns. A column is identified as in that check: the physical name of an
    /// ingested path, the name of a computed column. Nothing is taken from an archive that is not guarded (no table yet)
    /// or from a rollup (its columns are derived). A disabled archive that was activated before keeps its table, but the
    /// lifecycle adds columns to an activated archive only, so an addition there is refused.
    /// </summary>
    /// <exception cref="ArchiveSchemaImmutableException">The archive is disabled and the seed adds a column.</exception>
    public static IReadOnlyList<ArchiveSeedColumn> TakeColumnAdditions(ICkCacheService cacheService, string tenantId,
        CkTypeGraph ckType, RtEntity stored, RtEntityTcDto incoming)
    {
        if (!IsGuarded(stored) || IsRollup(ckType))
        {
            return [];
        }

        var attribute = incoming.Attributes.FirstOrDefault(a => NameOf(ckType, a) == ColumnsAttribute);
        if (attribute?.Value is not IEnumerable items || attribute.Value is string)
        {
            return [];
        }

        object? ToTransport(object? value) => ImportRtModelCommand.ToTransportValue(cacheService, tenantId, value);
        CkEnumGraph? ResolveEnum(CkId<CkEnumId> enumId) => cacheService.TryGetCkEnum(tenantId, enumId, out var e) ? e : null;
        CkRecordGraph? ResolveRecord(RtCkId<CkRecordId> recordId) => cacheService.TryGetRtCkRecord(tenantId, recordId, out var r) ? r : null;

        var existing = ToColumns(ToTransport(StoredValue(stored, ColumnsAttribute)), ResolveEnum, ResolveRecord);
        var kept = new List<object?>();
        var additions = new List<ArchiveSeedColumn>();
        foreach (var item in items)
        {
            if (item is not RtRecordTcDto record || KeyOf(record) is not { } key || existing.ContainsKey(key))
            {
                kept.Add(item);
                continue;
            }

            if (additions.Any(a => a.Key == key))
            {
                // The seed names the same new column twice (or two paths with one physical name): it is added once,
                // and the duplicate must not reach the definition either.
                continue;
            }

            var formula = MemberValue(record, FormulaMember) as string;
            additions.Add(new ArchiveSeedColumn(
                key,
                string.IsNullOrWhiteSpace(formula) ? MemberValue(record, PathMember) as string : null,
                MemberValue(record, NameMember) as string,
                string.IsNullOrWhiteSpace(formula) ? null : formula,
                ToFormulaResultType(MemberValue(record, ResultTypeMember)),
                MemberValue(record, IndexedMember) is bool indexed && indexed));
        }

        if (additions.Count == 0)
        {
            return [];
        }

        if (StatusOf(stored) != CkArchiveStatus.Activated)
        {
            throw Frozen(false, stored, ColumnsAttribute,
                $"column '{additions[0].Key}' can only be added while the archive is Activated; enable it first");
        }

        attribute.Value = kept;
        return additions;
    }

    private static string? KeyOf(RtRecordTcDto record)
    {
        var name = MemberValue(record, NameMember) as string;
        var path = MemberValue(record, PathMember) as string;
        return !string.IsNullOrWhiteSpace(path) ? RollupColumnGenerator.SanitisePath(path) : name;
    }

    /// <summary>The declared result type by name or key; <c>null</c> for a value that names no defined type.</summary>
    internal static FormulaResultType? ToFormulaResultType(object? value)
    {
        int? key = value switch
        {
            FormulaResultType type => (int)type,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            string text when Enum.TryParse<FormulaResultType>(text, ignoreCase: true, out var parsed) => (int)parsed,
            IConvertible convertible when value is not string => convertible.ToInt32(CultureInfo.InvariantCulture),
            _ => null,
        };
        return key is { } k && Enum.IsDefined(typeof(FormulaResultType), k) ? (FormulaResultType)k : null;
    }

    private static void Ensure(ICkCacheService cacheService, string tenantId, CkTypeGraph ckType, RtEntity stored,
        RtEntityTcDto incoming, bool partial)
    {
        if (!IsGuarded(stored))
        {
            return;
        }

        object? ToTransport(object? value) => ImportRtModelCommand.ToTransportValue(cacheService, tenantId, value);
        CkEnumGraph? ResolveEnum(CkId<CkEnumId> enumId) => cacheService.TryGetCkEnum(tenantId, enumId, out var e) ? e : null;
        CkRecordGraph? ResolveRecord(RtCkId<CkRecordId> recordId) => cacheService.TryGetRtCkRecord(tenantId, recordId, out var r) ? r : null;

        var changed = BlueprintEntityComparer.Compare(incoming, stored, (CkTypeWithAttributesGraph)ckType, ToTransport, ResolveEnum, ResolveRecord)
            .Select(c => c.AttributeName)
            .ToHashSet(StringComparer.Ordinal);
        var carried = incoming.Attributes.Select(a => NameOf(ckType, a)).ToHashSet(StringComparer.Ordinal);

        var rollup = IsRollup(ckType);
        foreach (var attribute in rollup ? FrozenRollupAttributes : FrozenBaseAttributes)
        {
            if (changed.Contains(attribute) && (!partial || carried.Contains(attribute)))
            {
                throw Frozen(rollup, stored, attribute, $"its {attribute} cannot be changed");
            }
        }

        object? IncomingOrStored(string attribute) =>
            incoming.Attributes.FirstOrDefault(a => NameOf(ckType, a) == attribute) is { } value
                ? value.Value
                : partial ? ToTransport(StoredValue(stored, attribute)) : null;

        if (rollup)
        {
            var before = EffectiveSources(ToTransport(StoredValue(stored, SourcesAttribute)), StoredValue(stored, SourceArchiveRtIdAttribute));
            var after = EffectiveSources(IncomingOrStored(SourcesAttribute), IncomingOrStored(SourceArchiveRtIdAttribute));
            if (!before.SequenceEqual(after, StringComparer.Ordinal))
            {
                throw Frozen(rollup, stored, SourcesAttribute, $"its {SourcesAttribute} cannot be changed");
            }

            return;
        }

        if (!partial || carried.Contains(ColumnsAttribute))
        {
            EnsureColumnsKept(stored,
                ToColumns(ToTransport(StoredValue(stored, ColumnsAttribute)), ResolveEnum, ResolveRecord),
                ToColumns(IncomingOrStored(ColumnsAttribute), ResolveEnum, ResolveRecord));
        }
    }

    private static void EnsureColumnsKept(RtEntity stored, Dictionary<string, string?> before, Dictionary<string, string?> after)
    {
        foreach (var (column, type) in before)
        {
            if (!after.TryGetValue(column, out var newType))
            {
                throw Frozen(false, stored, ColumnsAttribute, $"column '{column}' cannot be removed");
            }

            if (!string.Equals(type, newType, StringComparison.Ordinal))
            {
                throw Frozen(false, stored, ColumnsAttribute, $"the type of column '{column}' cannot be changed");
            }
        }
    }

    /// <summary>
    /// The columns by what identifies them in the table: the physical name of an ingested path, the name of a computed
    /// column. The type of an ingested column follows from its path; a computed one declares it.
    /// </summary>
    private static Dictionary<string, string?> ToColumns(object? value, Func<CkId<CkEnumId>, CkEnumGraph?> resolveEnum,
        Func<RtCkId<CkRecordId>, CkRecordGraph?> resolveRecord)
    {
        var columns = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var record in Records(value))
        {
            var key = KeyOf(record);
            if (key is null)
            {
                continue;
            }

            var resultType = MemberValue(record, ResultTypeMember);
            var memberGraph = resolveRecord(record.CkRecordId)?.AllAttributesByName.GetValueOrDefault(ResultTypeMember);
            columns[key] = memberGraph == null
                ? resultType?.ToString()
                : Convert.ToString(BlueprintEntityComparer.Normalise(resultType, memberGraph, resolveEnum), CultureInfo.InvariantCulture);
        }

        return columns;
    }

    /// <summary>
    /// The declared sources in one comparable form, whichever way they are stored: a non-empty <c>Sources</c> list wins,
    /// otherwise the deprecated scalar is one unbounded source.
    /// </summary>
    private static List<string> EffectiveSources(object? sources, object? scalar)
    {
        var references = Records(sources)
            .Select(r => string.Join("|",
                Text(MemberValue(r, SourceArchiveRtIdAttribute)),
                Text(MemberValue(r, "ValidFrom")),
                Text(MemberValue(r, "ValidTo"))))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (references.Count == 0 && !string.IsNullOrWhiteSpace(Text(scalar)))
        {
            references.Add($"{Text(scalar)}||");
        }

        return references;
    }

    private static string Text(object? value) => value switch
    {
        null => string.Empty,
        DateTime dateTime => dateTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.UtcTicks.ToString(CultureInfo.InvariantCulture),
        string text when DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) =>
            parsed.UtcTicks.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static IEnumerable<RtRecordTcDto> Records(object? value) => value switch
    {
        null or string => [],
        IEnumerable items => items.OfType<RtRecordTcDto>(),
        _ => [],
    };

    private static object? MemberValue(RtRecordTcDto record, string member)
        => record.Attributes.FirstOrDefault(a => a.Id.ToString().EndsWith("." + member, StringComparison.Ordinal))?.Value;

    private static string NameOf(CkTypeGraph ckType, RtAttributeTcDto attribute)
        => ckType.AllAttributes.Values.FirstOrDefault(a => a.CkAttributeId.ToRtCkId().Equals(attribute.Id))?.AttributeName ?? string.Empty;

    private static object? StoredValue(RtEntity stored, string attribute)
        => stored.Attributes.GetValueOrDefault(attribute);

    private static bool IsRollup(CkTypeGraph ckType)
        => ckType.AllAttributes.Values.Any(a => a.AttributeName == AggregationsAttribute);

    private static CkArchiveStatus StatusOf(RtEntity stored)
        => stored.Attributes.TryGetValue(StatusAttribute, out var status) && status != null
            ? (CkArchiveStatus)Convert.ToInt32(status, CultureInfo.InvariantCulture)
            : CkArchiveStatus.Created;

    private static bool IsGuarded(RtEntity stored)
        => stored.RtState != RtState.Archived && StatusOf(stored) switch
        {
            CkArchiveStatus.Activated => true,
            CkArchiveStatus.Disabled => HasValue(stored, FirstActivatedAtAttribute) || HasValue(stored, LastAggregatedBucketEndAttribute),
            _ => false
        };

    private static bool HasValue(RtEntity stored, string attribute)
        => stored.Attributes.TryGetValue(attribute, out var value) && value != null;

    private static void ThrowIfFrozen(CkTypeGraph ckType, RtEntity stored, string attribute)
    {
        if (!IsGuarded(stored))
        {
            return;
        }

        var rollup = IsRollup(ckType);
        var frozen = rollup
            ? FrozenRollupAttributes.Contains(attribute) || attribute == SourcesAttribute
            : attribute == ColumnsAttribute;
        if (frozen)
        {
            throw Frozen(rollup, stored, attribute, $"its {attribute} cannot be changed");
        }
    }

    private static StreamDataException Frozen(bool rollup, RtEntity stored, string attribute, string change)
    {
        var status = StatusOf(stored);
        return rollup
            ? new RollupSchemaImmutableException(stored.RtId, stored.RtWellKnownName, status, attribute)
            : new ArchiveSchemaImmutableException(stored.RtId, stored.RtWellKnownName, status, attribute, change);
    }
}

/// <summary>
/// AB#7997: a column a seed adds to an activated archive, taken out of the import by
/// <see cref="ArchiveDefinitionGuard.TakeColumnAdditions" /> and added through the archive lifecycle after the import.
/// </summary>
/// <param name="Key">How the column is identified in the table: the physical name of a path, the name of a computed column.</param>
/// <param name="Path">The attribute path of an ingested column; <c>null</c> for a computed column.</param>
/// <param name="Name">The declared name (required for a computed column).</param>
/// <param name="Formula">The formula of a computed column; <c>null</c> for an ingested column.</param>
/// <param name="ResultType">The declared result type of a computed column.</param>
/// <param name="Indexed">Whether the column is indexed.</param>
internal sealed record ArchiveSeedColumn(
    string Key, string? Path, string? Name, string? Formula, FormulaResultType? ResultType, bool Indexed)
{
    /// <summary>True for a computed column.</summary>
    public bool IsComputed => Formula is not null;
}
