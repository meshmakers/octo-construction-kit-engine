using System.Collections;
using System.Globalization;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
///     Decision D11 of the design note <c>docs/concept-number-ranges-and-released-documents.md</c>: once a number range
///     with a release configuration has handed out its first number, a write to the range must not unlock its documents.
///     A write to such a <c>System/AutoIncrement</c> entity is refused (message 168) when it removes <c>Release</c> (or
///     leaves it unreadable), changes <c>StateAttribute</c> or <c>StornoCkTypeId</c>, removes a value from
///     <c>ReleasedValues</c> or adds one to <c>ChangeableAttributes</c>, and the range itself is never deleted. Adding a
///     released value and removing a changeable attribute lock more and are allowed; before the first number everything
///     stays configurable. Called from the same places as <see cref="ReleasedDocumentLock" /> (every write of
///     <see cref="BulkRtMutation" /> and the bulk import), so no option and no identity skips it.
/// </summary>
internal static class NumberRangeReleaseLock
{
    internal const string CurrentValueAttribute = "CurrentValue";
    internal const string StartValueAttribute = "StartValue";
    internal const string PeriodsAttribute = "Periods";
    internal const string FiscalYearAttribute = "FiscalYear";

    /// <summary>Pseudo attribute name of a write that renames or clears the range's <c>RtWellKnownName</c>.</summary>
    internal const string RtWellKnownNameChange = "rtWellKnownName";

    /// <summary>State shown in the refusal of a range write.</summary>
    internal const string NumberedState = "numbered";

    /// <summary>One write to a range: the target, the operation and the range as the write leaves it (null: deleted).</summary>
    internal sealed record RangeWrite(RtEntityId Target, string Operation, Func<RtEntity, RtEntity?> Resulting);

    /// <summary>
    ///     Refuses the writes of <paramref name="writes" /> that change the release configuration of a numbered range.
    ///     Reads only when a write targets a <c>System/AutoIncrement</c> entity.
    /// </summary>
    public static async Task CheckAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        ICkCacheService ckCacheService, IReadOnlyList<RangeWrite> writes)
    {
        var rangeWrites = writes.Where(w => w.Target.RtId != OctoObjectId.Empty && IsRange(w.Target.CkTypeId))
            .ToList();
        if (rangeWrites.Count == 0 ||
            !ckCacheService.TryGetRtCkType(repositoryDataSource.TenantId, ReleasedDocumentLock.AutoIncrementCkTypeId,
                out var rangeGraph))
        {
            return;
        }

        var ids = rangeWrites.Select(w => w.Target.RtId).Distinct().ToList();
        var stored = (await repositoryDataSource.GetRtCollection<RtEntity>(rangeGraph)
                .FindManyAsync(session, e => ids.Contains(e.RtId)).ConfigureAwait(false))
            .ToDictionary(e => e.RtId);

        var result = new OperationResult();
        var refusals = new List<ReleasedDocumentRefusal>();
        foreach (var write in rangeWrites)
        {
            if (!stored.TryGetValue(write.Target.RtId, out var range))
            {
                continue;
            }

            var changes = ForbiddenChanges(range, write.Resulting(range));
            if (changes.Count == 0)
            {
                continue;
            }

            var id = new RtEntityId(range.CkTypeId ?? write.Target.CkTypeId, range.RtId);
            var name = range.RtWellKnownName ?? range.RtId.ToString();
            refusals.Add(new ReleasedDocumentRefusal(id, write.Operation, changes, NumberedState, name));
            NumberRangeRelease.TryCreate(range, out var release, out _);
            result.AddMessage(MessageCodes.RtReleasedDocumentLocked($"{id.CkTypeId.FullName}@{id.RtId}", id.RtId,
                id.CkTypeId.FullName, NumberedState, name, write.Operation, string.Join(", ", changes),
                string.Join(", ", (release?.ChangeableAttributes ?? []).Order(StringComparer.Ordinal))));
        }

        if (refusals.Count > 0)
        {
            throw new ReleasedDocumentLockedException(result, refusals);
        }
    }

    /// <summary>
    ///     The changes of the release configuration from <paramref name="stored" /> to <paramref name="resulting" /> that
    ///     D11 refuses; empty when the stored range has no readable release configuration or has not numbered yet.
    ///     <paramref name="resulting" /> null is a delete.
    /// </summary>
    public static List<string> ForbiddenChanges(RtEntity stored, RtEntity? resulting)
    {
        var changes = new List<string>();
        if (stored.GetAttributeValueOrDefault(NumberRangeRelease.ReleaseAttribute) == null ||
            !NumberRangeRelease.TryCreate(stored, out var before, out _) || before == null || !HasNumbered(stored))
        {
            return changes;
        }

        if (resulting == null)
        {
            changes.Add(ReleasedDocumentLock.RtStateChange);
            return changes;
        }

        // The lock and the number modifier find the range by its name: renaming or clearing it unlocks its documents.
        if (!string.Equals(stored.RtWellKnownName, resulting.RtWellKnownName, StringComparison.Ordinal))
        {
            changes.Add(RtWellKnownNameChange);
        }

        // A numbered range's counter only moves up: a lower counter, a lower or removed period, or a start value
        // raised above the next number would hand out issued numbers again (or unlock its documents once the range
        // counts as not numbered). The allocator only raises a counter and writes it outside this funnel.
        foreach (var change in CounterMovesBack(stored, resulting))
        {
            AddOnce(changes, change);
        }

        if (!HasNumbered(resulting))
        {
            foreach (var change in CounterChanges(stored, resulting))
            {
                AddOnce(changes, change);
            }
        }

        if (!NumberRangeRelease.TryCreate(resulting, out var after, out _) || after == null)
        {
            changes.Add(NumberRangeRelease.ReleaseAttribute);
            return changes;
        }

        if (!string.Equals(before.StateAttribute, after.StateAttribute, StringComparison.Ordinal))
        {
            changes.Add(Member(NumberRangeRelease.StateAttributeAttribute));
        }

        if (!string.Equals(StornoCkTypeIdOf(stored), StornoCkTypeIdOf(resulting), StringComparison.Ordinal))
        {
            changes.Add(Member(NumberRangeRelease.StornoCkTypeIdAttribute));
        }

        if (before.ReleasedValues.Except(after.ReleasedValues, StringComparer.Ordinal).Any())
        {
            changes.Add(Member(NumberRangeRelease.ReleasedValuesAttribute));
        }

        if (AddsChangeableAttributes(before, after))
        {
            changes.Add(Member(NumberRangeRelease.ChangeableAttributesAttribute));
        }

        return changes;
    }

    /// <summary>
    ///     The counter attributes a write moves back: <c>CurrentValue</c> lowered, a period's counter lowered or the
    ///     period removed, <c>StartValue</c> raised above the next number of the range.
    /// </summary>
    private static List<string> CounterMovesBack(RtEntity stored, RtEntity resulting)
    {
        var changes = new List<string>();
        var storedCurrent = ToLong(stored.GetAttributeValueOrDefault(CurrentValueAttribute)) ?? 0;
        if ((ToLong(resulting.GetAttributeValueOrDefault(CurrentValueAttribute)) ?? 0) < storedCurrent)
        {
            changes.Add(CurrentValueAttribute);
        }

        var storedPeriods = PeriodValues(stored);
        var resultingPeriods = PeriodValues(resulting);
        if (storedPeriods.Any(p => !resultingPeriods.TryGetValue(p.Key, out var value) || value < p.Value))
        {
            changes.Add(PeriodsAttribute);
        }

        var storedStart = ToLong(stored.GetAttributeValueOrDefault(StartValueAttribute));
        var resultingStart = ToLong(resulting.GetAttributeValueOrDefault(StartValueAttribute));
        var highest = Math.Max(storedCurrent, storedPeriods.Count > 0 ? storedPeriods.Values.Max() : 0);
        var nextNumber = Math.Max(highest, (storedStart ?? 1) - 1) + 1;
        if (resultingStart is { } start && start != storedStart && start > nextNumber)
        {
            changes.Add(StartValueAttribute);
        }

        return changes;
    }

    /// <summary>The counter of every period of the range, by fiscal year (a period without a year is keyed by its position).</summary>
    private static Dictionary<string, long> PeriodValues(RtEntity range)
    {
        var values = new Dictionary<string, long>(StringComparer.Ordinal);
        if (range.GetAttributeValueOrDefault(PeriodsAttribute) is IEnumerable periods and not string)
        {
            var position = 0;
            foreach (var period in periods)
            {
                if (period is RtTypeWithAttributes record)
                {
                    var year = record.GetAttributeValueOrDefault(FiscalYearAttribute);
                    var key = year == null
                        ? $"#{position}"
                        : Convert.ToString(year, CultureInfo.InvariantCulture) ?? $"#{position}";
                    values[key] = ToLong(record.GetAttributeValueOrDefault(CurrentValueAttribute)) ?? 0;
                }

                position++;
            }
        }

        return values;
    }

    private static void AddOnce(List<string> changes, string change)
    {
        if (!changes.Contains(change, StringComparer.Ordinal))
        {
            changes.Add(change);
        }
    }

    /// <summary>The counter attributes whose written value leaves the range without a handed-out number.</summary>
    private static List<string> CounterChanges(RtEntity stored, RtEntity resulting)
    {
        var changes = new List<string>();
        foreach (var name in new[] { CurrentValueAttribute, StartValueAttribute })
        {
            if (ToLong(stored.GetAttributeValueOrDefault(name)) != ToLong(resulting.GetAttributeValueOrDefault(name)))
            {
                changes.Add(name);
            }
        }

        if (MaxPeriodValue(stored) != MaxPeriodValue(resulting))
        {
            changes.Add(PeriodsAttribute);
        }

        if (changes.Count == 0)
        {
            changes.Add(CurrentValueAttribute);
        }

        return changes;
    }

    private static long? MaxPeriodValue(RtEntity range)
    {
        long? max = null;
        if (range.GetAttributeValueOrDefault(PeriodsAttribute) is IEnumerable periods and not string)
        {
            foreach (var period in periods)
            {
                if (period is RtTypeWithAttributes record &&
                    ToLong(record.GetAttributeValueOrDefault(CurrentValueAttribute)) is { } value &&
                    (max == null || value > max))
                {
                    max = value;
                }
            }
        }

        return max;
    }

    /// <summary>
    ///     The strict rule on <c>ChangeableAttributes</c> (decided by the developer): after the first number no attribute
    ///     may become changeable. Kept on its own so that it can be dropped without touching the other rules.
    /// </summary>
    private static bool AddsChangeableAttributes(NumberRangeRelease before, NumberRangeRelease after) =>
        after.ChangeableAttributes.Except(before.ChangeableAttributes, StringComparer.Ordinal).Any();

    /// <summary>
    ///     True when the range has handed out a number: a period whose counter reached the first number of a period
    ///     (<c>StartValue</c>, or 1), or a counter that reached <c>StartValue</c> - without a start value any counter above
    ///     0, which also counts a counter configured above 0 as numbered (fail closed).
    /// </summary>
    public static bool HasNumbered(RtEntity range)
    {
        var startValue = ToLong(range.GetAttributeValueOrDefault(StartValueAttribute));
        if (range.GetAttributeValueOrDefault(PeriodsAttribute) is IEnumerable periods and not string)
        {
            foreach (var period in periods)
            {
                if (period is RtTypeWithAttributes record &&
                    ToLong(record.GetAttributeValueOrDefault(CurrentValueAttribute)) is { } periodValue &&
                    periodValue >= (startValue ?? 1))
                {
                    return true;
                }
            }
        }

        var currentValue = ToLong(range.GetAttributeValueOrDefault(CurrentValueAttribute)) ?? 0;
        return startValue is { } start ? currentValue >= start : currentValue > 0;
    }

    /// <summary>
    ///     The range as a partial update leaves it: the stored attributes with the written ones laid over them. A member
    ///     path into <c>Release</c> cannot be laid over; it leaves the configuration unreadable, so it is refused on a
    ///     numbered range.
    /// </summary>
    public static RtEntity Overlay(RtEntity stored, RtEntity partial)
    {
        var resulting = new RtEntity(stored.GetRtCkTypeId(), stored.RtId, stored.Attributes)
        {
            // A partial update writes the name only when it carries one (MongoDataSourceMapper.ApplyUpdate).
            RtWellKnownName = string.IsNullOrWhiteSpace(partial.RtWellKnownName)
                ? stored.RtWellKnownName
                : partial.RtWellKnownName
        };
        foreach (var (name, value) in partial.Attributes)
        {
            if (name.StartsWith(NumberRangeRelease.ReleaseAttribute + ".", StringComparison.Ordinal))
            {
                resulting.SetAttributeRawValue(NumberRangeRelease.ReleaseAttribute, "member path write");
                continue;
            }

            resulting.SetAttributeRawValue(name, value);
        }

        return resulting;
    }

    private static bool IsRange(RtCkId<CkTypeId>? ckTypeId) =>
        ckTypeId != null && string.Equals(ckTypeId.FullName, ReleasedDocumentLock.AutoIncrementCkTypeId.FullName,
            StringComparison.Ordinal);

    private static string Member(string name) => $"{NumberRangeRelease.ReleaseAttribute}.{name}";

    private static string? StornoCkTypeIdOf(RtEntity range) =>
        (range.GetAttributeValueOrDefault(NumberRangeRelease.ReleaseAttribute) as RtTypeWithAttributes)
        ?.GetAttributeValueOrDefault(NumberRangeRelease.StornoCkTypeIdAttribute) as string;

    private static long? ToLong(object? value)
    {
        try
        {
            return value == null ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }
}
