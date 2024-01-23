namespace Meshmakers.Octo.ConstructionKit.Contracts.Services;

/// <summary>
/// Interface of services that provides the corresponding class type for a given CkTypeId or CkRecordId
/// </summary>
public interface ICkClassMappingService
{
    /// <summary>
    /// Returns the class type for the given CkTypeId
    /// </summary>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <returns>The corresponding class type if there is a mapping</returns>
    Type? GetCkTypeClass(CkId<CkTypeId> ckTypeId);
    
    /// <summary>
    /// Returns the class type for the given CkRecordId
    /// </summary>
    /// <param name="ckRecordId">Construction kit record id</param>
    /// <returns>The corresponding class type if there is a mapping</returns>
    Type? GetCkRecordClass(CkId<CkRecordId> ckRecordId);
}