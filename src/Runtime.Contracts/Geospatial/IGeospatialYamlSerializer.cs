using Meshmakers.Octo.Runtime.Contracts.Geospatial.Geometry;

namespace Meshmakers.Octo.Runtime.Contracts.Geospatial;

/// <summary>
/// Interface for serializing geospatial object types to YAML format.
/// </summary>
public interface IGeospatialYamlSerializer
{
    /// <summary>
    /// Serializes the specified object to YAML format.
    /// </summary>
    /// <param name="obj">The object to serialize.</param>
    /// <typeparam name="T">The type of object to serialize.</typeparam>
    /// <returns>Serialized YAML string.</returns>
    string Serialize<T>(T obj)
        where T: GeoJSONObject;

    /// <summary>
    /// Deserializes the specified YAML string to an object of the specified type.
    /// </summary>
    /// <param name="yaml">The YAML string to deserialize.</param>
    /// <typeparam name="T">The type of object to deserialize.</typeparam>
    /// <returns>Data object of the specified type.</returns>
    T Deserialize<T>(string yaml)
        where T: GeoJSONObject;
}