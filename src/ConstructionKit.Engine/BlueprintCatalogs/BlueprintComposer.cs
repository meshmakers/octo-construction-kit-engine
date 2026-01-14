using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;

/// <summary>
/// Composes multiple blueprints into a single resolved blueprint.
/// </summary>
internal class BlueprintComposer : IBlueprintComposer
{
    private readonly IBlueprintCatalogManager _catalogManager;
    private readonly ILogger<BlueprintComposer> _logger;

    /// <summary>
    /// Creates a new instance of <see cref="BlueprintComposer"/>
    /// </summary>
    /// <param name="catalogManager">Blueprint catalog manager</param>
    /// <param name="logger">Logger</param>
    public BlueprintComposer(
        IBlueprintCatalogManager catalogManager,
        ILogger<BlueprintComposer> logger)
    {
        _catalogManager = catalogManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ComposedBlueprintDto> ComposeAsync(
        BlueprintId blueprintId,
        OperationResult operationResult,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Composing blueprint {BlueprintId}", blueprintId);

        var resolvedBlueprints = new List<BlueprintMetaRootDto>();
        var visited = new HashSet<string>();

        // Resolve blueprint hierarchy (depth-first, base blueprints first)
        await ResolveRecursivelyAsync(blueprintId, resolvedBlueprints, visited, operationResult, cancellationToken)
            .ConfigureAwait(false);

        // Merge CK dependencies from all blueprints
        var mergedCkDependencies = MergeCkDependencies(resolvedBlueprints);

        // Collect seed data references in order (base blueprints first)
        var seedDataReferences = CollectSeedDataReferences(resolvedBlueprints);

        // Collect migration references from the root blueprint only
        var migrationReferences = CollectMigrationReferences(resolvedBlueprints.LastOrDefault());

        _logger.LogInformation(
            "Composed blueprint {BlueprintId}: {BlueprintCount} blueprints, {CkDependencyCount} CK dependencies, {SeedDataCount} seed data files, {MigrationCount} migrations",
            blueprintId,
            resolvedBlueprints.Count,
            mergedCkDependencies.Count,
            seedDataReferences.Count,
            migrationReferences.Count);

        return new ComposedBlueprintDto
        {
            RootBlueprintId = blueprintId,
            CkModelDependencies = mergedCkDependencies,
            SeedDataReferences = seedDataReferences,
            ResolvedBlueprints = resolvedBlueprints,
            AvailableMigrations = migrationReferences
        };
    }

    /// <inheritdoc />
    public async Task<ComposedBlueprintDto> ComposeAsync(
        BlueprintIdVersionRange blueprintIdVersionRange,
        OperationResult operationResult,
        CancellationToken cancellationToken = default)
    {
        // Resolve version range to exact version
        var existingResult = await _catalogManager.IsExistingAsync(blueprintIdVersionRange).ConfigureAwait(false);

        if (!existingResult.Exists || existingResult.BlueprintId == null)
        {
            operationResult.AddMessage(new OperationMessage(
                MessageLevel.Error,
                null,
                10,
                $"Blueprint '{blueprintIdVersionRange}' not found in any catalog"));
            throw BlueprintCatalogException.BlueprintNotFound(new BlueprintId(blueprintIdVersionRange.Name, "0.0.0"));
        }

        return await ComposeAsync(existingResult.BlueprintId, operationResult, cancellationToken).ConfigureAwait(false);
    }

    private async Task ResolveRecursivelyAsync(
        BlueprintId blueprintId,
        List<BlueprintMetaRootDto> resolvedBlueprints,
        HashSet<string> visited,
        OperationResult operationResult,
        CancellationToken cancellationToken)
    {
        var blueprintKey = blueprintId.FullName;

        // Check for circular reference
        if (visited.Contains(blueprintKey))
        {
            operationResult.AddMessage(new OperationMessage(
                MessageLevel.Error,
                null,
                11,
                $"Circular reference detected for blueprint '{blueprintId}'"));
            throw BlueprintCatalogException.CircularBlueprintReference(blueprintId);
        }

        visited.Add(blueprintKey);

        _logger.LogDebug("Resolving blueprint {BlueprintId}", blueprintId);

        // Get the blueprint
        var blueprint = await _catalogManager.GetAsync(blueprintId, operationResult).ConfigureAwait(false);

        // Resolve seed data path
        if (!string.IsNullOrEmpty(blueprint.SeedDataPath))
        {
            var blueprintPath = await _catalogManager.GetBlueprintPathAsync(blueprintId).ConfigureAwait(false);
            // The seed data path will be resolved later when applying
        }

        // First resolve composed blueprints (depth-first, so base blueprints come first)
        if (blueprint.ComposedBlueprints != null)
        {
            foreach (var composedBlueprintRange in blueprint.ComposedBlueprints)
            {
                // Resolve version range to exact version
                var existingResult = await _catalogManager.IsExistingAsync(composedBlueprintRange).ConfigureAwait(false);

                if (!existingResult.Exists || existingResult.BlueprintId == null)
                {
                    operationResult.AddMessage(new OperationMessage(
                        MessageLevel.Error,
                        null,
                        12,
                        $"Composed blueprint '{composedBlueprintRange}' not found"));
                    continue;
                }

                await ResolveRecursivelyAsync(existingResult.BlueprintId, resolvedBlueprints, visited, operationResult, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        // Add this blueprint after its dependencies (so base blueprints come first in list)
        resolvedBlueprints.Add(blueprint);
    }

    private static List<CkModelIdVersionRange> MergeCkDependencies(List<BlueprintMetaRootDto> blueprints)
    {
        var merged = new Dictionary<string, CkModelIdVersionRange>();

        foreach (var blueprint in blueprints)
        {
            if (blueprint.CkModelDependencies == null)
            {
                continue;
            }

            foreach (var dep in blueprint.CkModelDependencies)
            {
                if (!merged.ContainsKey(dep.Name))
                {
                    // First occurrence, use as-is
                    merged[dep.Name] = dep;
                }
                else
                {
                    // Already have this dependency, take the one with higher minimum version
                    // This is a simplified merge strategy - in practice you might want more sophisticated version range merging
                    var existing = merged[dep.Name];
                    // For now, we keep the first one encountered (base blueprint takes precedence)
                    // More sophisticated merging could be added here
                }
            }
        }

        return merged.Values.ToList();
    }

    private List<SeedDataReferenceDto> CollectSeedDataReferences(List<BlueprintMetaRootDto> blueprints)
    {
        var references = new List<SeedDataReferenceDto>();

        foreach (var blueprint in blueprints)
        {
            if (string.IsNullOrEmpty(blueprint.SeedDataPath))
            {
                continue;
            }

            references.Add(new SeedDataReferenceDto
            {
                BlueprintId = blueprint.BlueprintId,
                SeedDataPath = blueprint.SeedDataPath!,
                ResolvedPath = null // Will be resolved when applying
            });
        }

        return references;
    }

    private static List<MigrationReferenceDto> CollectMigrationReferences(BlueprintMetaRootDto? blueprint)
    {
        var references = new List<MigrationReferenceDto>();

        if (blueprint?.Migrations == null)
        {
            return references;
        }

        foreach (var migration in blueprint.Migrations)
        {
            references.Add(new MigrationReferenceDto
            {
                BlueprintId = blueprint.BlueprintId,
                FromVersion = migration.FromVersion,
                ScriptPath = migration.ScriptPath,
                Description = migration.Description,
                ResolvedPath = null // Will be resolved when applying
            });
        }

        return references;
    }
}
