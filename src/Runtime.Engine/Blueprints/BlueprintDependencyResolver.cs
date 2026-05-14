using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
/// Walks <see cref="BlueprintMetaRootDto.BlueprintDependencies"/> depth-first
/// from a root blueprint, deduplicates shared dependencies, surfaces
/// circular / missing / version-mismatch conflicts, and produces a
/// topo-sorted install order (base dependencies first, root last).
/// </summary>
internal sealed class BlueprintDependencyResolver : IBlueprintDependencyResolver
{
    private readonly IBlueprintCatalogManager _catalogManager;
    private readonly ILogger<BlueprintDependencyResolver> _logger;

    public BlueprintDependencyResolver(
        IBlueprintCatalogManager catalogManager,
        ILogger<BlueprintDependencyResolver> logger)
    {
        _catalogManager = catalogManager;
        _logger = logger;
    }

    public async Task<BlueprintResolutionResult> ResolveAsync(
        BlueprintId rootBlueprintId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Resolving dependency graph for {RootBlueprintId}", rootBlueprintId);

        var state = new ResolutionState();
        await VisitAsync(rootBlueprintId, state, cancellationToken).ConfigureAwait(false);

        var success = state.Conflicts.Count == 0;

        return new BlueprintResolutionResult
        {
            RootBlueprintId = rootBlueprintId,
            Success = success,
            InstallOrder = success ? state.InstallOrder : [],
            Conflicts = state.Conflicts,
            Warnings = state.Warnings
        };
    }

    /// <summary>
    /// Depth-first visit: enter node, recurse into deps, then append the
    /// blueprint to <see cref="ResolutionState.InstallOrder"/>. That ordering
    /// is the topo sort: when we append, all dependencies are already in.
    /// </summary>
    private async Task VisitAsync(
        BlueprintId blueprintId,
        ResolutionState state,
        CancellationToken cancellationToken)
    {
        var name = blueprintId.Name;

        // Already-visited check on name+version: same id -> dedupe; different
        // version of the same name -> version conflict.
        if (state.VisitedByName.TryGetValue(name, out var alreadyVisited))
        {
            if (alreadyVisited.FullName == blueprintId.FullName)
            {
                return;
            }

            state.Conflicts.Add(new BlueprintResolutionConflict
            {
                ConflictType = BlueprintResolutionConflictType.IncompatibleDependencyVersions,
                OffendingBlueprintId = blueprintId,
                Description =
                    $"Blueprint '{name}' is required in two incompatible versions: " +
                    $"'{alreadyVisited.FullName}' and '{blueprintId.FullName}'.",
                AdditionalContext =
                    $"resolved={alreadyVisited.FullName}, requested={blueprintId.FullName}"
            });
            return;
        }

        if (!state.PathStack.Add(name))
        {
            state.Conflicts.Add(new BlueprintResolutionConflict
            {
                ConflictType = BlueprintResolutionConflictType.CircularDependency,
                OffendingBlueprintId = blueprintId,
                Description = $"Circular dependency detected on blueprint '{name}'.",
                AdditionalContext = $"cycle={string.Join(" -> ", state.PathOrder)} -> {name}"
            });
            return;
        }
        state.PathOrder.Add(name);

        BlueprintMetaRootDto blueprint;
        try
        {
            var operationResult = new OperationResult();
            blueprint = await _catalogManager.GetAsync(blueprintId, operationResult).ConfigureAwait(false);
            if (operationResult.HasErrors || operationResult.HasFatalErrors)
            {
                state.Conflicts.Add(new BlueprintResolutionConflict
                {
                    ConflictType = BlueprintResolutionConflictType.MissingDependency,
                    OffendingBlueprintId = blueprintId,
                    Description = $"Blueprint '{blueprintId.FullName}' could not be loaded from any catalog.",
                    AdditionalContext = string.Join("; ", operationResult.Messages
                        .Where(m => m.MessageLevel != MessageLevel.Info)
                        .Select(m => m.MessageText))
                });
                ExitPath(state, name);
                return;
            }
        }
        catch (Exception ex)
        {
            state.Conflicts.Add(new BlueprintResolutionConflict
            {
                ConflictType = BlueprintResolutionConflictType.MissingDependency,
                OffendingBlueprintId = blueprintId,
                Description = $"Blueprint '{blueprintId.FullName}' could not be loaded from any catalog.",
                AdditionalContext = ex.Message
            });
            ExitPath(state, name);
            return;
        }

        foreach (var depRange in blueprint.BlueprintDependencies ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();

            var existing = await _catalogManager.IsExistingAsync(depRange).ConfigureAwait(false);
            if (!existing.Exists || existing.BlueprintId == null)
            {
                state.Conflicts.Add(new BlueprintResolutionConflict
                {
                    ConflictType = BlueprintResolutionConflictType.MissingDependency,
                    OffendingBlueprintId = null,
                    Description =
                        $"Blueprint '{blueprintId.FullName}' depends on '{depRange}' but no matching " +
                        "version was found in any catalog.",
                    AdditionalContext = $"requiredBy={blueprintId.FullName}, range={depRange}"
                });
                continue;
            }

            await VisitAsync(existing.BlueprintId, state, cancellationToken).ConfigureAwait(false);
        }

        // Post-order append → topo sort (deps already added).
        state.InstallOrder.Add(blueprint);
        state.VisitedByName[name] = blueprintId;
        ExitPath(state, name);
    }

    private static void ExitPath(ResolutionState state, string name)
    {
        state.PathStack.Remove(name);
        if (state.PathOrder.Count > 0 && state.PathOrder[state.PathOrder.Count - 1] == name)
        {
            state.PathOrder.RemoveAt(state.PathOrder.Count - 1);
        }
    }

    private sealed class ResolutionState
    {
        /// <summary>Blueprints already fully visited, indexed by name.</summary>
        public Dictionary<string, BlueprintId> VisitedByName { get; } = new(StringComparer.Ordinal);

        /// <summary>Names currently on the recursion stack (for cycle detection).</summary>
        public HashSet<string> PathStack { get; } = new(StringComparer.Ordinal);

        /// <summary>Ordered version of <see cref="PathStack"/> for nice error messages.</summary>
        public List<string> PathOrder { get; } = [];

        public List<BlueprintMetaRootDto> InstallOrder { get; } = [];
        public List<BlueprintResolutionConflict> Conflicts { get; } = [];
        public List<string> Warnings { get; } = [];
    }
}
