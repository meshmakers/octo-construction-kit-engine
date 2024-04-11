using Meshmakers.Octo.Runtime.Contracts.Geospatial.Converters;

namespace Meshmakers.Octo.Runtime.Contracts.Geospatial.CoordinateReferenceSystem;

/// <summary>
/// Base Interface for CRSBase Object types.
/// </summary>
public interface ICRSObject
{
    /// <summary>
    /// Gets the CRS type.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyOrder(2)]
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    [System.Text.Json.Serialization.JsonConverter(typeof(JsonCamelCasingStringEnumConverter))]
    CRSType Type { get; }

    /// <summary>
    /// Gets the properties.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyOrder(1)]
    [System.Text.Json.Serialization.JsonPropertyName("properties")] 
    Dictionary<string, object?> Properties { get; }
}