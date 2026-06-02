using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
/// Persists <see cref="ITenantBlueprintHistory"/> entries as
/// <c>System/BlueprintHistory</c> CK entities inside the tenant's own runtime
/// repository — there is no cross-tenant collection. Each apply (install /
/// update / rollback / re-apply) becomes one append-only entity in the tenant
/// database; this keeps the audit trail co-located with the seed entities it
/// describes.
/// </summary>
public sealed class EntityTenantBlueprintHistory : ITenantBlueprintHistory
{
    private static readonly RtCkId<CkTypeId> HistoryCkTypeId =
        new("System", "BlueprintHistory");

    private const string AttrBlueprintName = "BlueprintName";
    private const string AttrBlueprintVersion = "BlueprintVersion";
    private const string AttrAppliedAt = "AppliedAt";
    private const string AttrApplicationMode = "ApplicationMode";
    private const string AttrPreviousBlueprintName = "PreviousBlueprintName";
    private const string AttrPreviousVersion = "PreviousVersion";
    private const string AttrEntitiesCreated = "EntitiesCreated";
    private const string AttrEntitiesUpdated = "EntitiesUpdated";
    private const string AttrEntitiesDeleted = "EntitiesDeleted";
    private const string AttrSeedDataChecksum = "SeedDataChecksum";

    private readonly IRuntimeRepositoryProvider _repositoryProvider;
    private readonly ILogger<EntityTenantBlueprintHistory> _logger;

    /// <summary>
    /// Creates a new <see cref="EntityTenantBlueprintHistory"/>.
    /// </summary>
    public EntityTenantBlueprintHistory(
        IRuntimeRepositoryProvider repositoryProvider,
        ILogger<EntityTenantBlueprintHistory> logger)
    {
        _repositoryProvider = repositoryProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TenantBlueprintInfo>> GetHistoryAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting blueprint history for tenant {TenantId}", tenantId);

        var repository = await _repositoryProvider
            .GetRepositoryAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
        if (repository == null)
        {
            return [];
        }

        var session = await repository.GetSessionAsync().ConfigureAwait(false);
        var options = RtEntityQueryOptions.Create();
        options.SortOrder(AttrAppliedAt, SortOrders.Descending);

        var resultSet = await repository
            .GetRtEntitiesByTypeAsync(session, HistoryCkTypeId, options)
            .ConfigureAwait(false);

        return resultSet.Items.Select(MapToInfo).ToList();
    }

    /// <inheritdoc />
    public async Task<TenantBlueprintInfo?> GetCurrentAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting current blueprint for tenant {TenantId}", tenantId);

        var repository = await _repositoryProvider
            .GetRepositoryAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
        if (repository == null)
        {
            return null;
        }

        var session = await repository.GetSessionAsync().ConfigureAwait(false);
        var options = RtEntityQueryOptions.Create();
        options.SortOrder(AttrAppliedAt, SortOrders.Descending);

        var resultSet = await repository
            .GetRtEntitiesByTypeAsync(session, HistoryCkTypeId, options, skip: 0, take: 1)
            .ConfigureAwait(false);

        var entity = resultSet.Items.FirstOrDefault();
        return entity == null ? null : MapToInfo(entity);
    }

    /// <inheritdoc />
    public async Task AddEntryAsync(
        string tenantId,
        TenantBlueprintInfo info,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Adding blueprint history entry for tenant {TenantId}: {BlueprintId}",
            tenantId, info.BlueprintId);

        var repository = await _repositoryProvider
            .GetRepositoryAsync(tenantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"No runtime repository available for tenant '{tenantId}'; cannot append blueprint history.");

        var session = await repository.GetSessionAsync().ConfigureAwait(false);
        session.StartTransaction();
        try
        {
            var entity = await repository
                .CreateTransientRtEntityByRtCkIdAsync(HistoryCkTypeId)
                .ConfigureAwait(false);

            entity.SetAttributeValue(AttrBlueprintName,
                AttributeValueTypesDto.String, info.BlueprintId.Name);
            entity.SetAttributeValue(AttrBlueprintVersion,
                AttributeValueTypesDto.String, info.BlueprintId.Version.ToString());
            entity.SetAttributeValue(AttrAppliedAt,
                AttributeValueTypesDto.DateTime, info.AppliedAt);
            entity.SetAttributeValue(AttrApplicationMode,
                AttributeValueTypesDto.String, info.ApplicationMode.ToString());
            entity.SetAttributeValue(AttrEntitiesCreated,
                AttributeValueTypesDto.Int, info.EntitiesCreated);
            entity.SetAttributeValue(AttrEntitiesUpdated,
                AttributeValueTypesDto.Int, info.EntitiesUpdated);
            entity.SetAttributeValue(AttrEntitiesDeleted,
                AttributeValueTypesDto.Int, info.EntitiesDeleted);

            if (info.PreviousVersion != null)
            {
                entity.SetAttributeValue(AttrPreviousBlueprintName,
                    AttributeValueTypesDto.String, info.PreviousVersion.Name);
                entity.SetAttributeValue(AttrPreviousVersion,
                    AttributeValueTypesDto.String, info.PreviousVersion.Version.ToString());
            }

            if (!string.IsNullOrEmpty(info.SeedDataChecksum))
            {
                entity.SetAttributeValue(AttrSeedDataChecksum,
                    AttributeValueTypesDto.String, info.SeedDataChecksum);
            }

            await repository
                .InsertOneRtEntityAsync(session, HistoryCkTypeId, entity)
                .ConfigureAwait(false);

            await session.CommitTransactionAsync().ConfigureAwait(false);
        }
        catch
        {
            await session.AbortTransactionAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> HasBlueprintAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Checking if tenant {TenantId} has a blueprint", tenantId);

        var repository = await _repositoryProvider
            .GetRepositoryAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
        if (repository == null)
        {
            return false;
        }

        var session = await repository.GetSessionAsync().ConfigureAwait(false);
        var resultSet = await repository
            .GetRtEntitiesByTypeAsync(session, HistoryCkTypeId, RtEntityQueryOptions.Create(),
                skip: 0, take: 1)
            .ConfigureAwait(false);

        return resultSet.Items.Any();
    }

    private static TenantBlueprintInfo MapToInfo(RtEntity entity)
    {
        var name = entity.GetAttributeStringValueOrDefault(AttrBlueprintName) ?? string.Empty;
        var version = entity.GetAttributeStringValueOrDefault(AttrBlueprintVersion) ?? string.Empty;
        var modeText = entity.GetAttributeStringValueOrDefault(AttrApplicationMode);

        BlueprintId? previous = null;
        var prevName = entity.GetAttributeStringValueOrDefault(AttrPreviousBlueprintName);
        var prevVersion = entity.GetAttributeStringValueOrDefault(AttrPreviousVersion);
        if (!string.IsNullOrEmpty(prevName) && !string.IsNullOrEmpty(prevVersion))
        {
            previous = new BlueprintId(prevName!, prevVersion!);
        }

        return new TenantBlueprintInfo
        {
            BlueprintId = new BlueprintId(name, version),
            AppliedAt = entity.GetAttributeValueOrDefault<DateTime>(AttrAppliedAt) ?? DateTime.MinValue,
            ApplicationMode = Enum.TryParse<BlueprintApplicationMode>(modeText, out var mode)
                ? mode
                : BlueprintApplicationMode.Initial,
            PreviousVersion = previous,
            EntitiesCreated = entity.GetAttributeValueOrDefault<int>(AttrEntitiesCreated) ?? 0,
            EntitiesUpdated = entity.GetAttributeValueOrDefault<int>(AttrEntitiesUpdated) ?? 0,
            EntitiesDeleted = entity.GetAttributeValueOrDefault<int>(AttrEntitiesDeleted) ?? 0,
            SeedDataChecksum = entity.GetAttributeStringValueOrDefault(AttrSeedDataChecksum)
        };
    }
}
