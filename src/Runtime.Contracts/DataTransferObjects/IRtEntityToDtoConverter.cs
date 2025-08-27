using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.DataTransferObjects;

/// <summary>
/// Interface for converting RtEntity to RtEntityDto
/// </summary>
public interface IRtEntityToDtoConverter
{
    /// <summary>
    /// Converts a RtEntity to RtEntityDto
    /// </summary>
    /// <param name="tenantId">Tenant Id</param>
    /// <param name="rtEntity">The RtEntity to convert</param>
    /// <returns></returns>
    /// <exception cref="PersistenceException">Thrown if the CkTypeId is undefined</exception>
    RtEntityDto Convert(string tenantId, RtEntity rtEntity);
}