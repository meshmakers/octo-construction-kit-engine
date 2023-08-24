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

    /// <summary>
    /// Returns a list of known construction kit model repositories
    /// </summary>
    /// <returns></returns>
    IEnumerable<Tuple<string, string>> GetRepositoryList();

    /// <summary>
    /// Publishes a model to a repository
    /// </summary>
    /// <param name="repositoryName">Name of Repository.</param>
    /// <param name="ckCompiledModelRoot">Deserialized construction kit model.</param>
    /// <param name="isForced">When true, existing construction kit models are replaced.</param>
    /// <returns></returns>
    Task PublishModelAsync(string repositoryName, CkCompiledModelRoot ckCompiledModelRoot, bool isForced);
}