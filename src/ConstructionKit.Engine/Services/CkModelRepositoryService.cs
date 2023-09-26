using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.ModelRepositories;

namespace Meshmakers.Octo.ConstructionKit.Engine.Services;

/// <summary>
/// Manages the CK model repositories
/// </summary>
internal class CkModelRepositoryService : ICkModelRepositoryService
{
    private readonly ICkModelRepositoryManager _ckModelRepositoryManager;

    /// <summary>
    /// Creates a new instance of the <see cref="CkModelRepositoryService"/> class.
    /// </summary>
    /// <param name="ckModelRepositoryManager"></param>
    public CkModelRepositoryService(ICkModelRepositoryManager ckModelRepositoryManager)
    {
        _ckModelRepositoryManager = ckModelRepositoryManager;
    }
    
    public async Task<CkCompiledModelRoot?> LookupCkModelAsync(CkModelId ckModelId, OperationResult operationResult, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        return await _ckModelRepositoryManager.LookupCkModelAsync(ckModelId, operationResult, sourceIdentifier, cancellationToken);
    }

    public IEnumerable<Tuple<string, string>> GetRepositoryList(object? sourceIdentifier = null)
    {
        return _ckModelRepositoryManager.GetRepositoryList(sourceIdentifier);
    }

    public async Task PublishModelAsync(string repositoryName, CkCompiledModelRoot ckCompiledModel, bool isForced, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        await _ckModelRepositoryManager.PublishModelAsync(repositoryName, ckCompiledModel, isForced, sourceIdentifier, cancellationToken);
    }

    public async Task UpdateModelAsync(string repositoryName, CkCompiledModelRoot ckCompiledModel, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        await _ckModelRepositoryManager.UpdateModelAsync(repositoryName, ckCompiledModel, sourceIdentifier, cancellationToken);
    }
}