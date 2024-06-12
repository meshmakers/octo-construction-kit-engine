using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

/// <summary>
/// Tools that build a directory from a given path and get relative destination directories
/// </summary>
public interface IDirectoryTools
{
    /// <summary>
    /// Build the directory according to given path and modelID
    /// </summary>
    /// <param name="documentPath">Determines where new directory is built</param>
    /// <param name="ckModelId">Determines file tree of directory</param>
    void BuildDirectory(string documentPath, CkModelId ckModelId);
    
    /// <summary>
    /// Gets the relative directory of a given path
    /// </summary>
    /// <param name="directoryPath">Path that the relative directory is built from</param>
    /// <returns></returns>
    string GetRelativeDestinationDirectory(string directoryPath);
}