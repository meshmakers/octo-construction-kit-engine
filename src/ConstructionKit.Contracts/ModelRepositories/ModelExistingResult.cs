namespace Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;

/// <summary>
/// Result of checking if a model exists in a model repository
/// </summary>
public record ModelExistingResult
{
    /// <summary>
    /// Indicates if the model exists
    /// </summary>
    public required bool Exists { get; init; }


    /// <summary>
    /// When <see cref="Exists"/> is <c>true</c>, the actual <see cref="CkModelId"/> of the existing model.
    /// </summary>
    public CkModelId? ModelId { get; init; }
}