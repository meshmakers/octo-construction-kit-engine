using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
/// Persists <see cref="ITenantBlueprintInstallations"/> entries as
/// <c>System/BlueprintInstallation</c> CK entities inside the tenant's own
/// runtime repository — there is no cross-tenant collection. Installation
/// rows therefore travel with the tenant (backup/restore, deletion) and an
/// apply in tenant X never writes to another tenant's database.
/// </summary>
public sealed class EntityTenantBlueprintInstallations : ITenantBlueprintInstallations
{
    private static readonly RtCkId<CkTypeId> InstallationCkTypeId =
        new("System", "BlueprintInstallation");

    private const string AttrBlueprintName = "BlueprintName";
    private const string AttrBlueprintVersion = "BlueprintVersion";
    private const string AttrInstalledAt = "InstalledAt";
    private const string AttrLastUpdatedAt = "LastUpdatedAt";
    private const string AttrSeedDataChecksum = "SeedDataChecksum";
    private const string AttrIsDependency = "IsDependency";
    private const string AttrResolvedDependencies = "ResolvedDependencies";

    private readonly IRuntimeRepositoryProvider _repositoryProvider;
    private readonly ILogger<EntityTenantBlueprintInstallations> _logger;

    /// <summary>
    /// Creates a new <see cref="EntityTenantBlueprintInstallations"/>.
    /// </summary>
    public EntityTenantBlueprintInstallations(
        IRuntimeRepositoryProvider repositoryProvider,
        ILogger<EntityTenantBlueprintInstallations> logger)
    {
        _repositoryProvider = repositoryProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BlueprintInstallation>> GetInstalledAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Listing blueprint installations for tenant {TenantId}", tenantId);

        var repository = await _repositoryProvider
            .GetRepositoryAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
        if (repository == null)
        {
            return [];
        }

        var session = await repository.GetSessionAsync().ConfigureAwait(false);
        var resultSet = await repository
            .GetRtEntitiesByTypeAsync(session, InstallationCkTypeId, RtEntityQueryOptions.Create())
            .ConfigureAwait(false);

        return resultSet.Items.Select(MapToInstallation).ToList();
    }

    /// <inheritdoc />
    public async Task<BlueprintInstallation?> GetByBlueprintNameAsync(
        string tenantId,
        string blueprintName,
        CancellationToken cancellationToken = default)
    {
        var repository = await _repositoryProvider
            .GetRepositoryAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
        if (repository == null)
        {
            return null;
        }

        var session = await repository.GetSessionAsync().ConfigureAwait(false);
        var existing = await FindByBlueprintNameAsync(repository, session, blueprintName)
            .ConfigureAwait(false);

        return existing == null ? null : MapToInstallation(existing);
    }

    /// <inheritdoc />
    public async Task UpsertAsync(
        string tenantId,
        BlueprintInstallation installation,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Upserting blueprint installation for tenant {TenantId}: {BlueprintId} (dependency={IsDependency})",
            tenantId, installation.BlueprintId, installation.IsDependency);

        var repository = await _repositoryProvider
            .GetRepositoryAsync(tenantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"No runtime repository available for tenant '{tenantId}'; cannot upsert blueprint installation.");

        var session = await repository.GetSessionAsync().ConfigureAwait(false);
        session.StartTransaction();
        try
        {
            var existing = await FindByBlueprintNameAsync(repository, session, installation.BlueprintId.Name)
                .ConfigureAwait(false);

            var entity = await repository
                .CreateTransientRtEntityByRtCkIdAsync(InstallationCkTypeId)
                .ConfigureAwait(false);
            entity.RtWellKnownName = installation.BlueprintId.Name;

            entity.SetAttributeValue(AttrBlueprintName,
                AttributeValueTypesDto.String, installation.BlueprintId.Name);
            entity.SetAttributeValue(AttrBlueprintVersion,
                AttributeValueTypesDto.String, installation.BlueprintId.Version.ToString());
            entity.SetAttributeValue(AttrInstalledAt,
                AttributeValueTypesDto.DateTime, installation.InstalledAt);
            entity.SetAttributeValue(AttrLastUpdatedAt,
                AttributeValueTypesDto.DateTime, installation.LastUpdatedAt);
            entity.SetAttributeValue(AttrIsDependency,
                AttributeValueTypesDto.Boolean, installation.IsDependency);

            if (!string.IsNullOrEmpty(installation.SeedDataChecksum))
            {
                entity.SetAttributeValue(AttrSeedDataChecksum,
                    AttributeValueTypesDto.String, installation.SeedDataChecksum);
            }

            if (installation.ResolvedDependencies.Count > 0)
            {
                var deps = installation.ResolvedDependencies
                    .Select(b => b.FullName)
                    .ToArray();
                entity.SetAttributeValue(AttrResolvedDependencies,
                    AttributeValueTypesDto.StringArray, deps);
            }

            if (existing != null)
            {
                await repository
                    .ReplaceOneRtEntityByIdAsync(session, InstallationCkTypeId, existing.RtId, entity)
                    .ConfigureAwait(false);
            }
            else
            {
                await repository
                    .InsertOneRtEntityAsync(session, InstallationCkTypeId, entity)
                    .ConfigureAwait(false);
            }

            await session.CommitTransactionAsync().ConfigureAwait(false);
        }
        catch
        {
            await session.AbortTransactionAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(
        string tenantId,
        string blueprintName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Removing blueprint installation for tenant {TenantId}: {BlueprintName}",
            tenantId, blueprintName);

        var repository = await _repositoryProvider
            .GetRepositoryAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
        if (repository == null)
        {
            return false;
        }

        var session = await repository.GetSessionAsync().ConfigureAwait(false);
        var existing = await FindByBlueprintNameAsync(repository, session, blueprintName)
            .ConfigureAwait(false);
        if (existing == null)
        {
            return false;
        }

        await repository
            .DeleteOneRtEntityByRtIdAsync(session, InstallationCkTypeId, existing.RtId, DeleteOptions.Erase)
            .ConfigureAwait(false);
        return true;
    }

    private static async Task<RtEntity?> FindByBlueprintNameAsync(
        IRuntimeRepository repository,
        IOctoSession session,
        string blueprintName)
    {
        var options = RtEntityQueryOptions.Create();
        options.Field(AttrBlueprintName, FieldFilterOperator.Equals, blueprintName);

        var resultSet = await repository
            .GetRtEntitiesByTypeAsync(session, InstallationCkTypeId, options, skip: 0, take: 1)
            .ConfigureAwait(false);

        return resultSet.Items.FirstOrDefault();
    }

    private static BlueprintInstallation MapToInstallation(RtEntity entity)
    {
        var name = entity.GetAttributeStringValueOrDefault(AttrBlueprintName)
            ?? entity.RtWellKnownName
            ?? string.Empty;
        var version = entity.GetAttributeStringValueOrDefault(AttrBlueprintVersion) ?? string.Empty;

        var resolved = entity.GetAttributeStringValuesOrDefault(AttrResolvedDependencies);

        return new BlueprintInstallation
        {
            BlueprintId = new BlueprintId(name, version),
            InstalledAt = entity.GetAttributeValueOrDefault<DateTime>(AttrInstalledAt) ?? DateTime.MinValue,
            LastUpdatedAt = entity.GetAttributeValueOrDefault<DateTime>(AttrLastUpdatedAt) ?? DateTime.MinValue,
            SeedDataChecksum = entity.GetAttributeStringValueOrDefault(AttrSeedDataChecksum),
            IsDependency = entity.GetAttributeValueOrDefault<bool>(AttrIsDependency) ?? false,
            ResolvedDependencies = resolved == null
                ? []
                : resolved.Select(s => new BlueprintId(s)).ToList()
        };
    }
}
