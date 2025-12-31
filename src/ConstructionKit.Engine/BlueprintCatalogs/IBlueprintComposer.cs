using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;

/// <summary>
/// Composes multiple blueprints into a single resolved blueprint by resolving
/// the blueprint hierarchy and merging CK dependencies and seed data references.
/// </summary>
public interface IBlueprintComposer
{
    /// <summary>
    /// Composes a blueprint by resolving its hierarchy and merging dependencies.
    /// </summary>
    /// <param name="blueprintId">The root blueprint to compose</param>
    /// <param name="operationResult">Operation results</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The composed blueprint with all dependencies and seed data references merged</returns>
    Task<ComposedBlueprintDto> ComposeAsync(
        BlueprintId blueprintId,
        OperationResult operationResult,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Composes a blueprint from a version range by first resolving the exact version.
    /// </summary>
    /// <param name="blueprintIdVersionRange">The blueprint version range to compose</param>
    /// <param name="operationResult">Operation results</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The composed blueprint with all dependencies and seed data references merged</returns>
    Task<ComposedBlueprintDto> ComposeAsync(
        BlueprintIdVersionRange blueprintIdVersionRange,
        OperationResult operationResult,
        CancellationToken cancellationToken = default);
}
