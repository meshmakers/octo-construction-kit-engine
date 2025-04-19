namespace Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

/// <summary>
/// Defines the type of binary storage.
/// </summary>
public enum BinaryType
{
    /// <summary>
    /// Binaries acting as a part of the file system
    /// </summary>
    FileSystem = 0,

    /// <summary>
    /// Binaries stored in a cache
    /// </summary>
    Cache = 1,
}