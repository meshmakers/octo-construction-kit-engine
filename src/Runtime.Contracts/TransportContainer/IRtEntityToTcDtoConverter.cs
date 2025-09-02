using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;

namespace Meshmakers.Octo.Runtime.Contracts.TransportContainer;

/// <summary>
/// Interface for converting RtEntity to RtEntityDto
/// </summary>
public interface IRtEntityToTcDtoConverter
{
    /// <summary>
    /// Converts a RtEntity to RtEntityDto
    /// </summary>
    /// <param name="tenantId">Tenant Id</param>
    /// <param name="rtEntity">The RtEntity to convert</param>
    /// <param name="attributeValueResolveFlags">Defines how attribute values are resolved</param>
    /// <returns></returns>
    /// <exception cref="PersistenceException">Thrown if the CkTypeId is undefined</exception>
    RtEntityTcDto Convert(string tenantId, RtEntity rtEntity,
        AttributeValueResolveFlags attributeValueResolveFlags = AttributeValueResolveFlags.Default);
}