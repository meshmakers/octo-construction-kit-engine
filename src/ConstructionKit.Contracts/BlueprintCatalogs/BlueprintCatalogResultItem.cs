using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

/// <summary>
/// Represents a single item in a blueprint catalog result
/// </summary>
public class BlueprintCatalogResultItem : BlueprintPropertiesDto
{
    /// <summary>
    /// Returns the name of the catalog the blueprint belongs to
    /// </summary>
    public required string CatalogName { get; init; }
}
