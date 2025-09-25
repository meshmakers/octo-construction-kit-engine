using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelRepositories;

/// <summary>
///     Manages the repositories that can be used to look up a compiled model.
/// </summary>
internal class CkModelRepositoryManager : ICkModelRepositoryManager
{
    private readonly ILogger<CkModelRepositoryManager> _logger;
    private readonly IEnumerable<ICkModelRepository> _ckModelRepositories;

    /// <summary>
    ///     Creates a new instance of the <see cref="CkModelRepositoryManager" /> class.
    /// </summary>
    /// <param name="logger">Logger for this class.</param>
    /// <param name="ckModelRepositories">List of construction kit model repositories.</param>
    public CkModelRepositoryManager(ILogger<CkModelRepositoryManager> logger,
        IEnumerable<ICkModelRepository> ckModelRepositories)
    {
        _logger = logger;
        _ckModelRepositories = ckModelRepositories;
    }

    /// <inheritdoc />
    public async Task<CkCompiledModelRoot?> LookupCkModelAsync(CkModelId ckModelId, OperationResult operationResult,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        _logger.LogInformation("Looking up CK model with id {CkModelId} in repositories", ckModelId);

        foreach (var ckModelRepository in _ckModelRepositories.OrderBy(x => x.Order))
        {
            if (!ckModelRepository.IsSupportingSourceIdentifier(sourceIdentifier))
            {
                continue;
            }

            _logger.LogInformation("Checking repository {RepositoryName} for model {CkModelId}",
                ckModelRepository.RepositoryName, ckModelId);

            var hasBeenFound = await ckModelRepository.IsModelIdExistingAsync(ckModelId, sourceIdentifier)
                .ConfigureAwait(false);
            if (hasBeenFound)
            {
                _logger.LogInformation("Found model {CkModelId} in repository {RepositoryName}", ckModelId, ckModelRepository.RepositoryName);
                return await ckModelRepository.GetModelAsync(ckModelId, operationResult, sourceIdentifier)
                    .ConfigureAwait(false);
            }
        }

        throw ModelRepositoryException.ModelNotFoundInRepositories(ckModelId);
    }

    /// <inheritdoc />
    public async Task<CkCompiledModelRoot?> LookupCkModelAsync(string repositoryName, CkModelId ckModelId,
        OperationResult operationResult,
        CancellationToken? cancellationToken = null)
    {
        _logger.LogInformation("Looking up CK model with id {CkModelId} in repository {RepositoryName}", ckModelId,
            repositoryName);

        var ckModelRepository = _ckModelRepositories.FirstOrDefault(x => string.Compare(x.RepositoryName,
            repositoryName, StringComparison.OrdinalIgnoreCase) == 0);
        if (ckModelRepository == null)
        {
            throw ModelRepositoryException.ModelRepositoryNotFound(repositoryName);
        }

        _logger.LogInformation("Checking repository {RepositoryName} for model {CkModelId}",
            ckModelRepository.RepositoryName, ckModelId);

        var hasBeenFound = await ckModelRepository.IsModelIdExistingAsync(ckModelId)
            .ConfigureAwait(false);
        if (hasBeenFound)
        {
            return await ckModelRepository.GetModelAsync(ckModelId, operationResult)
                .ConfigureAwait(false);
        }

        throw ModelRepositoryException.ModelNotFoundInRepositories(ckModelId);
    }

    /// <inheritdoc />
    public IEnumerable<Tuple<string, string>> GetRepositoryList(object? sourceIdentifier = null)
    {
        foreach (var ckModelRepository in _ckModelRepositories.OrderBy(x => x.Order))
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
        bool publishExtensions,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        var ckModelRepository = _ckModelRepositories.FirstOrDefault(x => string.Compare(x.RepositoryName,
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

        await ckModelRepository.PublishModelAsync(ckCompiledModel, isForced, publishExtensions, sourceIdentifier)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateModelAsync(string repositoryName, CkCompiledModelRoot ckCompiledModel,
        bool publishExtensions,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        var ckModelRepository = _ckModelRepositories.FirstOrDefault(x => string.Compare(x.RepositoryName,
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

        await ckModelRepository.UpdateModelAsync(ckCompiledModel, publishExtensions, sourceIdentifier)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CustomizeCkEnumAsync(string repositoryName, CkId<CkEnumId> ckEnumId,
        ICollection<CkEnumUpdate> ckEnumUpdates, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        var ckModelRepository = _ckModelRepositories.FirstOrDefault(x => string.Compare(x.RepositoryName,
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

        await ckModelRepository.CustomizeCkEnumAsync(ckEnumId, ckEnumUpdates, sourceIdentifier).ConfigureAwait(false);
    }

    public async Task<bool> IsCkModelExistingAsync(string repositoryName, CkModelId ckModelId, object? sourceIdentifier = null)
    {
        var ckModelRepository = _ckModelRepositories.FirstOrDefault(x => string.Compare(x.RepositoryName,
            repositoryName, StringComparison.OrdinalIgnoreCase) == 0);
        if (ckModelRepository == null)
        {
            throw ModelRepositoryException.ModelRepositoryNotFound(repositoryName);
        }

        return await ckModelRepository.IsModelIdExistingAsync(ckModelId, sourceIdentifier).ConfigureAwait(false);
    }

    public async Task<ModelExistingResult> IsCkModelExistingAsync(CkModelIdVersionRange ckModelIdVersionRange,
        object? sourceIdentifier = null)
    {
        foreach (var ckModelRepository in _ckModelRepositories.OrderBy(x => x.Order))
        {
            if (!ckModelRepository.IsSupportingSourceIdentifier(sourceIdentifier))
            {
                continue;
            }

            var modelExistingResult = await ckModelRepository.IsModelIdExistingAsync(ckModelIdVersionRange, sourceIdentifier).ConfigureAwait(false);
            if (modelExistingResult.Exists)
            {
                return modelExistingResult;
            }
        }

        return new ModelExistingResult { Exists = false  };
    }

    public async Task<ModelExistingResult> IsCkModelExistingAsync(string repositoryName, CkModelIdVersionRange ckModelIdVersionRange,
        object? sourceIdentifier = null)
    {
        var ckModelRepository = _ckModelRepositories.FirstOrDefault(x => string.Compare(x.RepositoryName,
            repositoryName, StringComparison.OrdinalIgnoreCase) == 0);
        if (ckModelRepository == null)
        {
            throw ModelRepositoryException.ModelRepositoryNotFound(repositoryName);
        }

        return await ckModelRepository.IsModelIdExistingAsync(ckModelIdVersionRange, sourceIdentifier).ConfigureAwait(false);
    }
}