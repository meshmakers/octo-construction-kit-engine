using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelRepositories;

internal class RepositoryManagementService(IModelRepository modelRepository)
    : IRepositoryManagementService, IRepositoryVersionLister, IRepositoryInstalledVersionLister
{
    /// <inheritdoc />
    public Task<IReadOnlyList<CkModelId>> ListVersionsAsync(string ckModelName, object? sourceIdentifier = null)
    {
        return modelRepository is IRepositoryVersionLister lister
            ? lister.ListVersionsAsync(ckModelName, sourceIdentifier)
            : Task.FromResult<IReadOnlyList<CkModelId>>([]);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RepositoryModelVersion>> ListInstalledVersionsAsync(string ckModelName,
        object? sourceIdentifier = null)
    {
        // Not supported is not "empty": the resolver falls back to the visible-versions text on this signal.
        return modelRepository is IRepositoryInstalledVersionLister lister
            ? lister.ListInstalledVersionsAsync(ckModelName, sourceIdentifier)
            : throw new NotSupportedException(
                "The model repository cannot list its installed versions with their state.");
    }

    public Task<bool> IsExistingAsync(CkModelId ckModelId, object? sourceIdentifier = null)
    {
        return modelRepository.IsExistingAsync(ckModelId, sourceIdentifier);
    }

    public Task<ModelExistingResult> IsExistingAsync(CkModelIdVersionRange ckModelIdVersionRange, object? sourceIdentifier = null)
    {
        return modelRepository.IsExistingAsync(ckModelIdVersionRange, sourceIdentifier);
    }

    public Task CustomizeCkEnumAsync(CkId<CkEnumId> ckEnumId, ICollection<CkEnumUpdate> ckEnumUpdates, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        return modelRepository.CustomizeCkEnumAsync(ckEnumId, ckEnumUpdates, sourceIdentifier);
    }

    public Task UpdateModelAsync(CkCompiledModelRoot ckCompiledModel, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        return modelRepository.UpdateModelAsync(ckCompiledModel, sourceIdentifier);
    }

    public Task<CkCompiledModelRoot?> TryLookupCkModelAsync(CkModelId ckModelId, OperationResult operationResult, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        return modelRepository.TryLookupCkModelAsync(ckModelId, operationResult, sourceIdentifier);
    }
}