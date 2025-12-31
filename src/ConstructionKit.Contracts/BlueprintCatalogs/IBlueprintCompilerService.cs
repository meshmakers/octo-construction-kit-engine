using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

/// <summary>
/// Service for blueprint compilation and management operations.
/// </summary>
public interface IBlueprintCompilerService
{
    /// <summary>
    /// Creates a new blueprint directory with template files.
    /// </summary>
    /// <param name="path">The path where the blueprint should be created.</param>
    /// <param name="blueprintName">The name of the blueprint.</param>
    /// <param name="version">The version of the blueprint (default: 1.0.0).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateNewAsync(string path, string blueprintName, string version = "1.0.0",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a blueprint directory and returns the parsed blueprint metadata.
    /// </summary>
    /// <param name="path">The path to the blueprint directory.</param>
    /// <param name="operationResult">The operation result for collecting messages.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The parsed blueprint metadata.</returns>
    Task<BlueprintMetaRootDto> ValidateAsync(string path, OperationResult operationResult,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Packs a blueprint directory into a distributable archive.
    /// </summary>
    /// <param name="path">The path to the blueprint directory.</param>
    /// <param name="outputPath">The output path for the packed blueprint.</param>
    /// <param name="operationResult">The operation result for collecting messages.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The path to the created archive.</returns>
    Task<string> PackAsync(string path, string outputPath, OperationResult operationResult,
        CancellationToken cancellationToken = default);
}
