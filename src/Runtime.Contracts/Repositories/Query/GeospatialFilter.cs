
namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
/// Base class for a geospatial filter
/// </summary>
public abstract class GeospatialFilter
{
    /// <summary>
    /// The attribute name to search for
    /// </summary>
    public string AttributeName { get; set; } = null!;
}