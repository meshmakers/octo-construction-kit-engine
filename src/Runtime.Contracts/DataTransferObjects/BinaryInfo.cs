using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.DataTransferObjects;

/// <summary>
/// Represents information about a binary.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public class BinaryInfoDto : IBinaryInfo
{
    /// <inheritdoc />
    public string ContentType { get; set; } = null!;

    /// <inheritdoc />
    public OctoObjectId BinaryId { get; set; }

    /// <inheritdoc />
    public string Filename  { get; set; } = null!;

    /// <inheritdoc />
    public DateTime UploadDateTime  { get; set; }

    /// <inheritdoc />
    public BinaryType BinaryType { get; set; }

    /// <inheritdoc />
    public long Size  { get; set; }
}