using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

public class CkAggregatedModelElements
{
    public CkAggregatedModelElements()
    {
        CkModelDependencies = new();
        CkTypes = new();
        CkAttributes = new();
        CkAssociationRoles = new();
    }

    public Dictionary<CkModelId, ICollection<CkModelId>> CkModelDependencies { get; }
    public Dictionary<CkId<CkTypeId>, CkTypeDto> CkTypes { get; }
    public Dictionary<CkId<CkAttributeId>, CkAttributeDto> CkAttributes { get; }
    public Dictionary<CkId<CkAssociationRoleId>, CkAssociationRoleDto> CkAssociationRoles { get; }

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
    }
}