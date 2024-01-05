using Meshmakers.Octo.Runtime.Contracts.Repositories.Local;

namespace Meshmakers.Octo.Runtime.Contracts.Services;

/// <summary>
///     Manages local repositories on the hard disk
/// </summary>
public interface ILocalRepositoryService
{
    /// <summary>
    ///     Creates a new repository at the given path
    /// </summary>
    /// <param name="repositoryPath"></param>
    /// <param name="tenantId"></param>
    /// <returns></returns>
    Task CreateRepositoryAsync(string repositoryPath, string tenantId);

    /// <summary>
    ///     Gets the repository at the given path
    /// </summary>
    /// <param name="repositoryPath"></param>
    /// <param name="tenantId"></param>
    /// <returns></returns>
    Task<ILocalRuntimeRepository> GetRepositoryAsync(string repositoryPath, string tenantId);


    /// <summary>
    ///     Deletes the gives repository
    /// </summary>
    /// <param name="tenantId">Tenant id of the </param>
    /// <returns></returns>
    Task DeleteRepositoryAsync(string tenantId);
}