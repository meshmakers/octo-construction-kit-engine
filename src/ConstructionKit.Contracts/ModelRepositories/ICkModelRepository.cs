using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;

public interface ICkModelRepository
{
    int Order { get; }
    string RepositoryName { get; }
    
    Task<bool> LookupModelIdAsync(CkModelId modelId);
    
    Task<CkCompiledModelRoot> GetModelAsync(CkModelId modelId);
    
    Task PublishModelAsync(CkCompiledModelRoot ckCompiledModel); 
    
    Task UpdateModelAsync(CkCompiledModelRoot ckCompiledModel);
}