using System.Text.Json.Serialization;

namespace Meshmakers.Octo.Runtime.Contracts.Geospatial.Geometry;

/// <summary>
/// Defines the Geographic Position type.
/// </summary>
/// <remarks>
/// See https://tools.ietf.org/html/rfc7946#section-3.1.1
/// </remarks>
public interface IPosition
{
    /// <summary>
    /// Gets the altitude.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? Altitude { get; }
    
    /// <summary>
    /// Gets the latitude.
    /// </summary>
    /// <value>The latitude.</value>
    double Latitude { get; }

    /// <summary>
    /// Gets the longitude.
    /// </summary>
    /// <value>The longitude.</value>
    double Longitude { get; }
}