namespace Meshmakers.Octo.ConstructionKit.Contracts.Services;

/// <summary>
/// Represents the result of a compilation
/// </summary>
public class CompileResult
{
    /// <summary>
    /// Creates a new instance of <see cref="CompileResult"/>
    /// </summary>
    /// <param name="compiledModelFile">Compiled model file</param>
    public CompileResult(string compiledModelFile)
    {
        CompiledModelFile = compiledModelFile;
    }
    
    /// <summary>
    /// Creates a new instance of <see cref="CompileResult"/>
    /// </summary>
    /// <param name="compiledModelFile">Compiled model file</param>
    /// <param name="compiledModelCacheFilePath">Path to the cache file</param>
    public CompileResult(string compiledModelFile, string? compiledModelCacheFilePath)
    : this(compiledModelFile)
    {
        CompiledModelCacheFilePath = compiledModelCacheFilePath;
    }
    
    /// <summary>
    /// Returns the path to the compiled model file
    /// </summary>
    public string CompiledModelFile { get;  }
    
    /// <summary>
    /// Returns if requested the path to the cache file
    /// </summary>
    public string? CompiledModelCacheFilePath { get; }
}