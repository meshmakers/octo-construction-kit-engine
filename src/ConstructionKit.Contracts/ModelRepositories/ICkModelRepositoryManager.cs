using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;

public interface ICkModelRepositoryManager
{
    public Task<CkCompiledModelRoot?> LookupCkModelAsync(CkModelId ckModelId);
}