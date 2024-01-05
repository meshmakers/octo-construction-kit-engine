namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Local;

/// <summary>
///     Represents a repository that is located on the local hard disk
/// </summary>
public interface ILocalRuntimeRepository : IRuntimeRepository
{
    /// <summary>
    ///     Returns the path of the directory that contains the repository
    /// </summary>
    string DirectoryPath { get; }
}