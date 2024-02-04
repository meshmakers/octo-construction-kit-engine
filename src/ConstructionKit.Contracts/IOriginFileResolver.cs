namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Resolves the location of the original file from a key.
/// </summary>
public interface IOriginFileResolver
{
    /// <summary>
    /// Resolves the location of the original file from a key.
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    string Resolve(object key);
    
    /// <summary>
    /// Adds a new location to the resolver.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="location"></param>
    void Add(object key, string location);
}