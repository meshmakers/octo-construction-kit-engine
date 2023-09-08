using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
/// Aggregates all model elements of multiple models.
/// </summary>
public class CkAggregatedModelElements
{
    /// <summary>
    /// Creates a new instance of <see cref="CkAggregatedModelElements"/>.
    /// </summary>
    public CkAggregatedModelElements()
    {
        CkModelDependencies = new();
        CkTypes = new();
        CkAttributes = new();
        CkAssociationRoles = new();
        CkRecords = new Dictionary<CkId<CkRecordId>, CkRecordDto>();
        CkEnums = new Dictionary<CkId<CkEnumId>, CkEnumDto>();
    }

    /// <summary>
    /// Returns a dictionary of model dependencies.
    /// </summary>
    public Dictionary<CkModelId, ICollection<CkModelId>> CkModelDependencies { get; }
    
    /// <summary>
    /// Returns a dictionary of CK types.
    /// </summary>
    public Dictionary<CkId<CkTypeId>, CkTypeDto> CkTypes { get; }
    
    /// <summary>
    /// Returns a dictionary of CK attributes.
    /// </summary>
    public Dictionary<CkId<CkAttributeId>, CkAttributeDto> CkAttributes { get; }
    
    /// <summary>
    /// Returns a dictionary of CK association roles.
    /// </summary>
    public Dictionary<CkId<CkAssociationRoleId>, CkAssociationRoleDto> CkAssociationRoles { get; }
    
    /// <summary>
    /// Returns a dictionary of CK records.
    /// </summary>
    public Dictionary<CkId<CkRecordId>, CkRecordDto> CkRecords { get; }
    
    /// <summary>
    /// Returns a dictionary of CK enums.
    /// </summary>
    public Dictionary<CkId<CkEnumId>, CkEnumDto> CkEnums { get; }

    /// <summary>
    /// Appends the model elements of the given <paramref name="ckCompiledModelRoot"/> to this instance.
    /// </summary>
    /// <param name="ckCompiledModelRoot">The compiled model root to append</param>
    public void AppendModel(CkCompiledModelRoot ckCompiledModelRoot)
    {
        CkModelDependencies.Add(ckCompiledModelRoot.ModelId, ckCompiledModelRoot.Dependencies ?? new List<CkModelId>());
        
        if (ckCompiledModelRoot.Attributes != null)
        {
            foreach (var ckAttribute in ckCompiledModelRoot.Attributes)
            {
                CkAttributes.Add(new CkId<CkAttributeId>(ckCompiledModelRoot.ModelId, ckAttribute.AttributeId), ckAttribute);
            }
        }
        
        if (ckCompiledModelRoot.AssociationRoles != null)
        {
            foreach (var ckAssociationRole in ckCompiledModelRoot.AssociationRoles)
            {
                CkAssociationRoles.Add(new CkId<CkAssociationRoleId>(ckCompiledModelRoot.ModelId, ckAssociationRole.AssociationRoleId), ckAssociationRole);
            }
        }
        
        if (ckCompiledModelRoot.Types != null)
        {
            foreach (var ckTypeDto in ckCompiledModelRoot.Types)
            {
                CkTypes.Add(new CkId<CkTypeId>(ckCompiledModelRoot.ModelId, ckTypeDto.TypeId), ckTypeDto);
            }
        }
                
        if (ckCompiledModelRoot.Records != null)
        {
            foreach (var ckRecordDto in ckCompiledModelRoot.Records)
            {
                CkRecords.Add(new CkId<CkRecordId>(ckCompiledModelRoot.ModelId, ckRecordDto.RecordId), ckRecordDto);
            }
        }
        
        if (ckCompiledModelRoot.Enums != null)
        {
            foreach (var ckEnumDto in ckCompiledModelRoot.Enums)
            {
                CkEnums.Add(new CkId<CkEnumId>(ckCompiledModelRoot.ModelId, ckEnumDto.EnumId), ckEnumDto);
            }
        }
    }
}