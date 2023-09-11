using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelRepositories;

/// <summary>
/// Manages the repositories that can be used to lookup a compiled model.
/// </summary>
internal class CkModelRepositoryManager : ICkModelRepositoryManager
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
    public async Task<CkCompiledModelRoot?> LookupCkModelAsync(CkModelId ckModelId, OperationResult operationResult,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        foreach (var ckModelRepository in _ckModelRepositories.OrderBy(x=> x.Order))
        {
            if (!ckModelRepository.IsSupportingSourceIdentifier(sourceIdentifier))
            {
                continue;
            }
            
            var hasBeenFound = await ckModelRepository.LookupModelIdAsync(ckModelId, sourceIdentifier);
            if (hasBeenFound)
            {
                return await ckModelRepository.GetModelAsync(ckModelId, operationResult, sourceIdentifier);
            }
        }

        throw ModelRepositoryException.ModelNotFoundInRepositories(ckModelId);
    }

    /// <inheritdoc />
    public IEnumerable<Tuple<string, string>> GetRepositoryList(object? sourceIdentifier = null)
    {
        foreach (var ckModelRepository in _ckModelRepositories.OrderBy(x=> x.Order))
        {
            if (!ckModelRepository.IsSupportingSourceIdentifier(sourceIdentifier))
            {
                continue;
            }
            
            yield return new Tuple<string, string>(ckModelRepository.RepositoryName, ckModelRepository.Description);
        }
    }

    /// <inheritdoc />
    public async Task PublishModelAsync(string repositoryName, CkCompiledModelRoot ckCompiledModel, bool isForced,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        var ckModelRepository = _ckModelRepositories.FirstOrDefault(x=> string.Compare(x.RepositoryName, 
            repositoryName, StringComparison.OrdinalIgnoreCase) == 0);
        if (ckModelRepository == null)
        {
            throw ModelRepositoryException.ModelRepositoryNotFound(repositoryName);
        }
        
        if (!ckModelRepository.IsSupportingSourceIdentifier(sourceIdentifier))
        {
            throw ModelRepositoryException.ModelRepositoryDoesNotSupportSourceIdentifier(repositoryName);
        }

        if (!ckModelRepository.CanWrite)
        {
            throw ModelRepositoryException.ModelRepositoryNotWritable(repositoryName);
        }

        await ckModelRepository.PublishModelAsync(ckCompiledModel, isForced, sourceIdentifier);
    }

    /// <inheritdoc />
    public async Task UpdateModelAsync(string repositoryName, CkCompiledModelRoot ckCompiledModel, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        var ckModelRepository = _ckModelRepositories.FirstOrDefault(x=> string.Compare(x.RepositoryName, 
            repositoryName, StringComparison.OrdinalIgnoreCase) == 0);
        if (ckModelRepository == null)
        {
            throw ModelRepositoryException.ModelRepositoryNotFound(repositoryName);
        }
        
        if (!ckModelRepository.IsSupportingSourceIdentifier(sourceIdentifier))
        {
            throw ModelRepositoryException.ModelRepositoryDoesNotSupportSourceIdentifier(repositoryName);
        }

        if (!ckModelRepository.CanWrite)
        {
            throw ModelRepositoryException.ModelRepositoryNotWritable(repositoryName);
        }
        
        await ckModelRepository.UpdateModelAsync(ckCompiledModel, sourceIdentifier);
    }
}