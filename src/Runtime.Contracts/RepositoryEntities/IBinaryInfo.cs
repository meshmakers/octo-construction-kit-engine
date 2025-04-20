using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

/// <summary>
/// Interface for information of a binary
/// </summary>
public interface IBinaryInfo
{
    /// <summary>
    /// Gets the content type of the binary
    /// </summary>
    public string ContentType { get; }

    /// <summary>
    /// Gets the object id of the binary
    /// </summary>
    public OctoObjectId BinaryId { get; }

    /// <summary>
    /// Gets the file name of the binary
    /// </summary>
    public string Filename { get; }

    /// <summary>
    /// Gets the upload date/time of the binary
    /// </summary>
    public DateTime UploadDateTime { get; }

    /// <summary>
    /// Gets the expiry date/time of the binary if applicable
    /// </summary>
    public DateTime? ExpiryDateTime { get; }

    /// <summary>
    /// Gets the binary type
    /// </summary>
    public BinaryType BinaryType { get; }

    /// <summary>
    /// Gets the runtime entity id of the binary
    /// </summary>
    public RtEntityId? RtEntityId { get;  }

    /// <summary>
    /// Gets the size of the binary in bytes
    /// </summary>
    public long Size { get; }
}