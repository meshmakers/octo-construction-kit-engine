namespace Meshmakers.Octo.ConstructionKit.Contracts.Services;

/// <summary>
///     Interface for the compiler service. That is the service that is responsible for compiling and managing the construction kit model.
/// </summary>
public interface ICompilerService
{
    /// <summary>
    ///     Creates a new construction kit model.
    /// </summary>
    /// <param name="rootPath">Local root path where the construction kit model is to be crated.</param>
    /// <returns></returns>
    Task CreateNewAsync(string rootPath);

    /// <summary>
    ///     Compiles the construction kit model.
    /// </summary>
    /// <param name="rootPath">Local root path where the construction kit model exists.</param>
    /// <param name="createCacheFile">
    ///     When true, a cache file is created parallel to the compiled construction kit model containing all
    ///     dependencies
    /// </param>
    /// <returns>An object with files created by compiler.</returns>
    Task<CompileResult> CompileAsync(string rootPath, bool createCacheFile);
}