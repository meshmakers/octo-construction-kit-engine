using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;

namespace Meshmakers.Octo.ConstructionKit.Compiler.ModelRepositories;

public class CkModelRepositoryManager : ICkModelRepositoryManager
{
    private readonly IEnumerable<ICkModelRepository> _ckModelRepositories;

    public CkModelRepositoryManager(IEnumerable<ICkModelRepository> ckModelRepositories)
    {
        _ckModelRepositories = ckModelRepositories;
    }
    
    public async Task<CkCompiledModelRoot?> LookupCkModelAsync(CkModelId ckModelId)
    {
        foreach (var ckModelRepository in _ckModelRepositories.OrderBy(x=> x.Order))
        {
            var hasBeenFound = await ckModelRepository.LookupModelIdAsync(ckModelId);
            if (hasBeenFound)
            {
                return await ckModelRepository.GetModelAsync(ckModelId);
            }
        }

        throw ModelRepositoryException.ModelNotFoundInRepositories(ckModelId);
    }
}