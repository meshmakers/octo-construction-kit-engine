using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;

namespace Meshmakers.Octo.ConstructionKit.SourceGeneration;

/// <summary>
/// Interface for construction kit type code generators
/// </summary>
public interface ICkTypeCodeGenerator : IEquatable<ICkTypeCodeGenerator>
{
   /// <summary>
   /// Generates code for a construction kit type
   /// </summary>
   /// <param name="ns">The namespace of the created code</param>
   /// <param name="ckModelId">The model id of the construction kit type</param>
   /// <param name="ckType">Construction kit type instance</param>
   /// <param name="cacheTenantId">Tenant id to access the correct cache instance</param>
   /// <param name="cacheService">The cache service</param>
   /// <returns></returns>
   string Generate(string ns, CkModelId ckModelId, CkTypeDto ckType, string cacheTenantId, ICkCacheService cacheService);
}
