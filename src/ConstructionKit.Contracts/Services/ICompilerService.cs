namespace Meshmakers.Octo.ConstructionKit.Contracts.Services;

/// <summary>
/// Interface for the compiler service. That is the service that is responsible for compiling and managing the construction kit model.
/// </summary>
public interface ICompilerService
{
    /// <summary>
    /// Creates a new construction kit model.
    /// </summary>
    /// <param name="rootPath">Local root path where the construction kit model is to be crated.</param>
    /// <returns></returns>
    Task CreateNewAsync(string rootPath);
    
    /// <summary>
    /// Compiles the construction kit model.
    /// </summary>
    /// <param name="rootPath">Local root path where the construction kit model exists.</param>
    /// <returns></returns>
    Task CompileAsync(string rootPath);
}