using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelRepositories;

/// <summary>
/// Manages the repositories that can be used to lookup a compiled model.
/// </summary>
public class CkModelRepositoryManager : ICkModelRepositoryManager
{
    private readonly IEnumerable<ICkModelRepository> _ckModelRepositories;

    /// <summary>
    /// Creates a new instance of the <see cref="CkModelRepositoryManager"/> class.
    /// </summary>
    /// <param name="ckModelRepositories"></param>
    public CkModelRepositoryManager(IEnumerable<ICkModelRepository> ckModelRepositories)
    {
        _ckModelRepositories = ckModelRepositories;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public IEnumerable<Tuple<string, string>> GetRepositoryList()
    {
        foreach (var ckModelRepository in _ckModelRepositories.OrderBy(x=> x.Order))
        {
            yield return new Tuple<string, string>(ckModelRepository.RepositoryName, ckModelRepository.Description);
        }
    }

    /// <inheritdoc />
    public async Task PublishModelAsync(string repositoryName, CkCompiledModelRoot ckCompiledModelRoot, bool isForced)
    {
        var ckModelRepository = _ckModelRepositories.FirstOrDefault(x=> string.Compare(x.RepositoryName, repositoryName, StringComparison.OrdinalIgnoreCase) == 0);
        if (ckModelRepository == null)
        {
            throw ModelRepositoryException.ModelRepositoryNotFound(repositoryName);
        }

        await ckModelRepository.PublishModelAsync(ckCompiledModelRoot, isForced);
    }
}