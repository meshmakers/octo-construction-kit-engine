namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

/// <summary>
/// Result of checking if a blueprint exists in a blueprint catalog
/// </summary>
public record BlueprintExistingResult
{
    /// <summary>
    /// Indicates if the blueprint exists
    /// </summary>
    public required bool Exists { get; init; }

    /// <summary>
    /// When <see cref="Exists"/> is <c>true</c>, the actual <see cref="BlueprintId"/> of the existing blueprint.
    /// </summary>
    public BlueprintId? BlueprintId { get; init; }
}
