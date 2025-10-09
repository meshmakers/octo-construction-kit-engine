using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Represents a single item in a model result
/// </summary>
public class CatalogResultItem : CkModelPropertiesDto
{
    /// <summary>
    /// Returns the name of the catalog the model belongs to
    /// </summary>
    public required string CatalogName { get; init; }
}