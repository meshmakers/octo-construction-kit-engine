using System.Collections;
using System.Globalization;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Secrets;

namespace Meshmakers.Octo.Runtime.Engine.Secrets;

/// <summary>
///     Implementation of <see cref="ISecretInventoryService" /> (AB#5532) over <see cref="SecretEntityScanner" />,
///     the scan the secret sweep uses. Read-only; classifies values with
///     <see cref="SecretValueStates.Describe(RtSecretValue?, Func{string?, bool}?, bool)" /> and the key ring of <see cref="ISecretAttributeProtector" />.
///     Never decrypts, never returns or logs a value.
/// </summary>
internal sealed class SecretInventoryService(
    IRuntimeRepositoryProvider repositoryProvider,
    ICkCacheService ckCacheService,
    ISecretAttributeProtector protector,
    ISecretWriteNormalizer writeNormalizer) : ISecretInventoryService
{
    private const int BatchSize = 500;
    private const string NameAttributeName = "Name";

    private readonly SecretEntityScanner _scanner = new(repositoryProvider, ckCacheService, writeNormalizer);

    /// <inheritdoc />
    public async Task<SecretInventoryPage> ListAsync(string tenantId, SecretInventoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(query);

        var forms = query.Forms is { Count: > 0 } ? new HashSet<SecretStorageForm>(query.Forms) : null;
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        var skip = Math.Max(0, query.Skip);
        var items = new List<SecretInventoryItem>();
        var total = 0;

        await foreach (var item in ScanAsync(tenantId, query.CkTypeId, cancellationToken).ConfigureAwait(false))
        {
            if ((forms != null && !forms.Contains(item.Form)) ||
                (query.NeedsReEntry != null && item.NeedsReEntry != query.NeedsReEntry.Value) ||
                (search != null && !Matches(item, search)))
            {
                continue;
            }

            if (total >= skip && items.Count < query.Take)
            {
                items.Add(item);
            }

            total++;
        }

        return new SecretInventoryPage(items, total);
    }

    /// <inheritdoc />
    public async Task<SecretInventorySummary> SummarizeAsync(string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var summary = new SecretInventorySummary();
        await foreach (var item in ScanAsync(tenantId, null, cancellationToken).ConfigureAwait(false))
        {
            summary.Add(item);
        }

        return summary;
    }

    private async IAsyncEnumerable<SecretInventoryItem> ScanAsync(string tenantId, string? ckTypeIdFilter,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var repository = await _scanner.GetRepositoryAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var types = _scanner.GetSecretTypes(tenantId, null);

        HashSet<string>? allowedTypes = null;
        if (ckTypeIdFilter != null)
        {
            if (!ckCacheService.TryGetRtCkType(tenantId, new RtCkId<CkTypeId>(ckTypeIdFilter.Trim()), out var filterType))
            {
                yield break;
            }

            allowedTypes = filterType.GetAllDerivedTypes(true).Select(id => id.FullName)
                .ToHashSet(StringComparer.Ordinal);
            types = types.Where(t => allowedTypes.Contains(t.CkTypeId.FullName)).ToList();
        }

        var session = await repository.GetSessionAsync().ConfigureAwait(false);
        var processed = new HashSet<OctoObjectId>();
        foreach (var type in types)
        {
            await foreach (var entities in _scanner.ReadPagesAsync(repository, session, type, BatchSize,
                               false, cancellationToken).ConfigureAwait(false))
            {
                foreach (var entity in entities)
                {
                    if (!processed.Add(entity.RtId))
                    {
                        continue;
                    }

                    var entityGraph = _scanner.ResolveEntityGraph(tenantId, entity, type);
                    if (allowedTypes != null && !allowedTypes.Contains(entityGraph.CkTypeId.FullName))
                    {
                        continue;
                    }

                    var context = new EntityContext(tenantId, entityGraph.CkTypeId.ToRtCkId().ToString(), entity,
                        ResolveDisplayName(entity, entityGraph));
                    foreach (var item in WalkEntity(context, entityGraph))
                    {
                        yield return item;
                    }
                }
            }
        }
    }

    private IEnumerable<SecretInventoryItem> WalkEntity(EntityContext context, CkTypeGraph entityGraph)
    {
        foreach (var attribute in entityGraph.AllAttributes.Values)
        {
            if (!writeNormalizer.AttributeHasSecrets(ckCacheService, context.TenantId, attribute))
            {
                continue;
            }

            context.Entity.Attributes.TryGetValue(attribute.AttributeName, out var value);
            var path = ToCamelCase(attribute.AttributeName);
            if (attribute.ValueType == AttributeValueTypesDto.Secret)
            {
                yield return CreateItem(context, attribute.AttributeName, path, !attribute.IsOptional, value);
                continue;
            }

            foreach (var item in WalkRecordValue(context, attribute.AttributeName, attribute, value, path))
            {
                yield return item;
            }
        }
    }

    private IEnumerable<SecretInventoryItem> WalkRecordValue(EntityContext context, string topLevelName,
        CkTypeAttributeGraph attribute, object? value, string path)
    {
        switch (value)
        {
            case null:
                yield break;
            case RtRecord record when attribute.ValueType == AttributeValueTypesDto.Record:
                foreach (var item in WalkRecord(context, topLevelName, attribute, record, path, null))
                {
                    yield return item;
                }

                yield break;
            case IEnumerable elements and not string when attribute.ValueType == AttributeValueTypesDto.RecordArray:
                var index = 0;
                foreach (var element in elements)
                {
                    if (element is RtRecord record)
                    {
                        var graph = _scanner.ResolveRecord(context.TenantId, record, attribute);
                        foreach (var item in WalkRecord(context, topLevelName, attribute, record,
                                     BuildElementPath(path, graph?.RecordKey, record, index), graph))
                        {
                            yield return item;
                        }
                    }

                    index++;
                }

                yield break;
        }
    }

    private IEnumerable<SecretInventoryItem> WalkRecord(EntityContext context, string topLevelName,
        CkTypeAttributeGraph attribute, RtRecord record, string path, CkRecordGraph? graph)
    {
        graph ??= _scanner.ResolveRecord(context.TenantId, record, attribute);
        if (graph == null)
        {
            yield break;
        }

        foreach (var member in graph.AllAttributes.Values)
        {
            if (!writeNormalizer.AttributeHasSecrets(ckCacheService, context.TenantId, member))
            {
                continue;
            }

            record.Attributes.TryGetValue(member.AttributeName, out var memberValue);
            var memberPath = path + "." + ToCamelCase(member.AttributeName);
            if (member.ValueType == AttributeValueTypesDto.Secret)
            {
                var item = CreateItem(context, topLevelName, memberPath, !member.IsOptional, memberValue);
                // An optional record member that is not set is not actionable (nothing to re-enter, e.g. a
                // Helm value override that is a plain value): omitted. Top-level optional secrets stay listed -
                // they are entity-level settings (AB#5532).
                if (item.Form != SecretStorageForm.NotSet || item.Required)
                {
                    yield return item;
                }

                continue;
            }

            foreach (var item in WalkRecordValue(context, topLevelName, member, memberValue, memberPath))
            {
                yield return item;
            }
        }
    }

    private SecretInventoryItem CreateItem(EntityContext context, string attributeName, string path, bool required,
        object? value)
    {
        var info = value switch
        {
            null => new SecretReadInfo(SecretValueState.NotSet, SecretStorageForm.NotSet, null, null),
            RtSecretValue secret => SecretValueStates.Describe(secret, protector.IsKnownKeyId, protector.IsLegacyV1KeyConfigured),
            // A plain string in a Secret slot is a legacy stored value.
            string text => SecretValueStates.Describe(RtSecretValue.LegacyPlaintext(text), protector.IsKnownKeyId, protector.IsLegacyV1KeyConfigured),
            // Not a value a Secret slot can hold.
            _ => new SecretReadInfo(SecretValueState.NotSet, SecretStorageForm.Corrupt, null, null)
        };

        return new SecretInventoryItem(context.CkTypeId, context.Entity.RtId, context.Entity.RtWellKnownName,
            context.DisplayName, path, attributeName, required, info.Form, info.KeyId, info.SetAt,
            SecretInventoryItem.IsReEntryNeeded(info.Form, required));
    }

    /// <summary>
    ///     Display name of an entity with a consistent fallback: the stored display name, then a string
    ///     <c>Name</c> attribute of the type, then the well-known name; null otherwise (callers show the rtId).
    /// </summary>
    internal static string? ResolveDisplayName(RtEntity entity, CkTypeGraph entityGraph)
    {
        if (!string.IsNullOrWhiteSpace(entity.RtDisplayName))
        {
            return entity.RtDisplayName;
        }

        var nameAttribute = entityGraph.AllAttributes.Values.FirstOrDefault(a =>
            a.ValueType == AttributeValueTypesDto.String &&
            string.Equals(a.AttributeName, NameAttributeName, StringComparison.Ordinal));
        if (nameAttribute != null &&
            entity.Attributes.TryGetValue(nameAttribute.AttributeName, out var name) &&
            name is string text && !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return string.IsNullOrWhiteSpace(entity.RtWellKnownName) ? null : entity.RtWellKnownName;
    }

    private static bool Matches(SecretInventoryItem item, string search)
    {
        return item.RtId.ToString().Contains(search, StringComparison.OrdinalIgnoreCase) ||
               item.CkTypeId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               (item.RtWellKnownName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (item.DisplayName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
               item.AttributePath.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     <c>name[key=value]</c> for a record array element with a record key (key name camelCase, value as
    ///     invariant text), <c>name[index]</c> otherwise. Record keys are identifiers, never secrets.
    /// </summary>
    internal static string BuildElementPath(string path, string? recordKey, RtRecord element, int index)
    {
        if (recordKey != null && element.Attributes.TryGetValue(recordKey, out var key) && key != null)
        {
            return $"{path}[{ToCamelCase(recordKey)}={Convert.ToString(key, CultureInfo.InvariantCulture)}]";
        }

        return $"{path}[{index.ToString(CultureInfo.InvariantCulture)}]";
    }

    internal static string ToCamelCase(string name)
    {
        return string.IsNullOrEmpty(name) || char.IsLower(name[0])
            ? name
            : char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    private sealed record EntityContext(string TenantId, string CkTypeId, RtEntity Entity, string? DisplayName);
}
