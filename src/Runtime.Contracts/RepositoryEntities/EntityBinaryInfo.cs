using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

/// <summary>
/// Represents information about a binary.
/// </summary>
public class EntityBinaryInfo
{
    /// <summary>
    /// Gets the content type of the binary
    /// </summary>
    public string ContentType { get; set; } = null!;

    /// <summary>
    /// Gets the object id of the binary
    /// </summary>
    public OctoObjectId? BinaryId { get; set; }

    /// <summary>
    /// Gets the file name of the binary
    /// </summary>
    public string Filename { get; set; } = null!;

    /// <summary>
    /// Gets the size of the binary in bytes
    /// </summary>
    public long? Size { get; set; }

    /// <summary>
    /// Stream to the binary data.
    /// </summary>
    [JsonIgnore]
    public Stream? Stream { get; set; }
}