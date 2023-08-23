using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;

/// <summary>
/// Manages the CK model repositories
/// </summary>
public interface ICkModelRepositoryManager
{
    /// <summary>
    /// Looks up a model by its id 
    /// </summary>
    /// <param name="ckModelId">The construction kit model id</param>
    /// <returns>If existing the deserialized and validated construction kit model</returns>
    public Task<CkCompiledModelRoot?> LookupCkModelAsync(CkModelId ckModelId);
}