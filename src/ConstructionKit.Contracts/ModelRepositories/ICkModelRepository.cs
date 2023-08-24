using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;

/// <summary>
/// Interface for a CK model repository
/// </summary>
public interface ICkModelRepository
{
    /// <summary>
    /// Returns the order of the repository that will be used to resolve construction kit models. The lower the order, the higher the priority.
    /// </summary>
    int Order { get; }
    
    /// <summary>
    /// Returns the name of the repository, used for identification. Repository names must be unique.
    /// </summary>
    string RepositoryName { get; }

    /// <summary>
    /// Returns the description of the repository, used for outputs including configuration information.
    /// </summary>
    public string Description { get; }
    
    /// <summary>
    /// Looks up a model by its id
    /// </summary>
    /// <param name="modelId">The construction kit model id</param>
    /// <returns>True if the model exists in this repository, otherwise false</returns>
    Task<bool> LookupModelIdAsync(CkModelId modelId);
    
    /// <summary>
    /// Gets a model by its id
    /// </summary>
    /// <param name="modelId">The construction kit model id</param>
    /// <returns>The deserialized and schema validated construction kit model</returns>
    Task<CkCompiledModelRoot> GetModelAsync(CkModelId modelId);

    /// <summary>
    /// Publishes a model to the repository
    /// </summary>
    /// <param name="ckCompiledModel">The validated construction kit model</param>
    /// <param name="force">Forces the operation by replacing model files if they exist.</param>
    /// <returns></returns>
    Task PublishModelAsync(CkCompiledModelRoot ckCompiledModel, bool force = false); 
    
    /// <summary>
    /// Updates a model in the repository
    /// </summary>
    /// <param name="ckCompiledModel">The validated construction kit model</param>
    /// <returns></returns>
    Task UpdateModelAsync(CkCompiledModelRoot ckCompiledModel);
}