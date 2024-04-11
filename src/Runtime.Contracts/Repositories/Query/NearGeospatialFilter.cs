
using Meshmakers.Common.Shared;
using Meshmakers.Octo.Runtime.Contracts.Geospatial.Geometry;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
/// Represents a filter value for a near field search
/// </summary>
public class NearGeospatialFilter : GeospatialFilter
{
    /// <summary>
    ///     Creates a new instance of <see cref="NearGeospatialFilter" />
    /// </summary>
    /// <param name="attributeName">The name of the attribute to compare</param>
    /// <param name="point">Point to search for</param>
    /// <param name="minDistance">The minimum distance from the center point that the documents can be.</param>
    /// <param name="maxDistance">The maximum distance from the center point that the documents can be.</param>
    public NearGeospatialFilter(string attributeName, Point point, double? minDistance, double? maxDistance)
    {
        ArgumentValidation.ValidateString(nameof(attributeName), attributeName);

        AttributeName = attributeName;
        Point = point;
        MinDistance = minDistance;
        MaxDistance = maxDistance;
    }
    
    /// <summary>
    /// The point to search for
    /// </summary>
    public Point Point { get; } 
    
    /// <summary>
    /// The maximum distance from the center point that the documents can be.
    /// </summary>
    public double? MaxDistance { get; }
    
    /// <summary>
    /// The minimum distance from the center point that the documents can be
    /// </summary>
    public double? MinDistance { get;  }
}
