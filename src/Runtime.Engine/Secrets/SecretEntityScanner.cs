using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine.Secrets;

/// <summary>
///     Shared scan of all entities holding Secret slots (AB#5532): the secret sweep
///     (<see cref="SecretMaintenanceService" />) and the secrets overview (<see cref="SecretInventoryService" />)
///     walk the same CK types in the same deterministic order.
/// </summary>
internal sealed class SecretEntityScanner(
    IRuntimeRepositoryProvider repositoryProvider,
    ICkCacheService ckCacheService,
    ISecretWriteNormalizer writeNormalizer)
{
    /// <summary>
    ///     Paging cursor of the scan: the runtime id (resolved to <c>_id</c> by the MongoDB field resolver).
    /// </summary>
    internal const string RtIdSortPath = "rtId";

    public ICkCacheService CkCacheService => ckCacheService;

    public ISecretWriteNormalizer WriteNormalizer => writeNormalizer;

    /// <summary>
    ///     The tenant repository with a loaded CK cache.
    /// </summary>
    public async Task<IRuntimeRepository> GetRepositoryAsync(string tenantId, CancellationToken cancellationToken)
    {
        var repository = await repositoryProvider.GetRepositoryAsync(tenantId, cancellationToken).ConfigureAwait(false)
                         ?? throw new InvalidOperationException($"No runtime repository is available for tenant '{tenantId}'.");
        if (!ckCacheService.IsTenantLoaded(tenantId))
        {
            await repository.LoadCacheForTenantAsync(ckCacheService).ConfigureAwait(false);
        }

        return repository;
    }

    /// <summary>
    ///     Concrete CK types with at least one Secret slot (top level or in a record at any depth), ordered
    ///     by full name; optionally only those of a model (or whose Secret attributes the model defines).
    /// </summary>
    public IReadOnlyList<CkTypeGraph> GetSecretTypes(string tenantId, string? ckModelName)
    {
        return ckCacheService.GetCkTypes(tenantId)
            .Where(t => !t.IsAbstract && writeNormalizer.HasSecretAttributes(ckCacheService, tenantId, t))
            .Where(t => ckModelName == null || BelongsToModel(tenantId, t, ckModelName))
            .OrderBy(t => t.CkTypeId.FullName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    ///     Reads the entities of one CK type page by page (sorted by rtId, caching disabled). A query of a
    ///     type may return entities of derived types as well; callers dedupe by rtId.
    /// </summary>
    /// <param name="repository">Tenant repository</param>
    /// <param name="session">Session</param>
    /// <param name="type">CK type to read</param>
    /// <param name="batchSize">Page size</param>
    /// <param name="includeArchived">
    ///     <c>true</c> for the sweep: archived (deleted, <c>RtState.Archived</c>) entities still hold stored
    ///     secrets that must be encrypted / cleaned up at rest. <c>false</c> for the inventory: like every public
    ///     query (<c>GetRtEntitiesByTypeAsync</c> without a global filter) it must not list deleted entities
    ///     (AB#5532/AB#5544).
    /// </param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async IAsyncEnumerable<IReadOnlyList<RtEntity>> ReadPagesAsync(IRuntimeRepository repository,
        IOctoSession session, CkTypeGraph type, int batchSize, bool includeArchived,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var skip = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Offset paging needs a deterministic order: without a
            // sort the backend may return pages in plan order, which is not guaranteed to be stable across
            // the queries of one scan, so an entity could be skipped or seen twice. rtId ("_id") is indexed
            // and never changes on a rewrite (AB#5532 review).
            var queryOptions = RtEntityQueryOptions.Create().Global(includeArchived).WithCachingDisabled()
                .SortOrder(RtIdSortPath, SortOrders.Ascending);
            var page = await repository.GetRtEntitiesByTypeAsync(session, type.CkTypeId.ToRtCkId(),
                queryOptions, skip, batchSize).ConfigureAwait(false);
            var entities = page.Items.ToList();
            if (entities.Count == 0)
            {
                yield break;
            }

            yield return entities;

            if (entities.Count < batchSize)
            {
                yield break;
            }

            skip += entities.Count;
        }
    }

    /// <summary>
    ///     The actual CK type of an entity (derived types), falling back to the queried type.
    /// </summary>
    public CkTypeGraph ResolveEntityGraph(string tenantId, RtEntity entity, CkTypeGraph queriedType)
    {
        return entity.CkTypeId != null && ckCacheService.TryGetRtCkType(tenantId, entity.CkTypeId, out var actual)
            ? actual
            : queriedType;
    }

    /// <summary>
    ///     The record definition of a record value: the element's own record id first (a record array may
    ///     hold derived records), then the declared record of the attribute.
    /// </summary>
    public CkRecordGraph? ResolveRecord(string tenantId, RtRecord record, CkTypeAttributeGraph attribute)
    {
        if (record.CkRecordId is { IsEmpty: false } &&
            ckCacheService.TryGetRtCkRecord(tenantId, record.CkRecordId, out var graph))
        {
            return graph;
        }

        return attribute.ValueCkRecordId != null &&
               ckCacheService.TryGetCkRecord(tenantId, attribute.ValueCkRecordId, out var declared)
            ? declared
            : null;
    }

    private bool BelongsToModel(string tenantId, CkTypeGraph type, string modelName)
    {
        if (string.Equals(type.CkTypeId.ModelId.Name, modelName, StringComparison.Ordinal))
        {
            return true;
        }

        return type.AllAttributes.Values.Any(a =>
            writeNormalizer.AttributeHasSecrets(ckCacheService, tenantId, a) &&
            string.Equals(a.CkAttributeId.ModelId.Name, modelName, StringComparison.Ordinal));
    }
}
