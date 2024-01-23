namespace Meshmakers.Octo.ConstructionKit.Contracts.Services;

/// <summary>
/// Interface for resolving classes from type/records ids.
/// </summary>
public interface ICkClassMap
{
    /// <summary>
    /// Returns the model id the mapping is for
    /// </summary>
    public CkModelId ModelId { get; }
    
    /// <summary>
    /// Returns the class type for the given type id
    /// </summary>
    /// <param name="ckTypeId">The type id within a model</param>
    /// <returns>The corresponding class type if there is a mapping</returns>
    Type? GetCkTypeClass(CkTypeId ckTypeId);
    
    /// <summary>
    /// Returns the class type for the given record id
    /// </summary>
    /// <param name="ckRecordId">The record id within a model</param>
    /// <returns>The corresponding class type if there is a mapping</returns>
    Type? GetCkRecordClass(CkRecordId ckRecordId);
}