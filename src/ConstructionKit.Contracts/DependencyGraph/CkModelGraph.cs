using System.Collections.ObjectModel;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

public class CkModelGraph
{
    private readonly IDictionary<CkId<CkTypeId>, CkTypeGraph> _entities;
    private readonly IDictionary<CkId<CkAttributeId>, CkAttributeGraph> _attributes;
    private readonly IDictionary<CkId<CkAssociationRoleId>, CkAssociationRoleGraph> _associationRoles;

    public CkModelGraph()
    {
        _entities = new Dictionary<CkId<CkTypeId>, CkTypeGraph>();
        _attributes = new Dictionary<CkId<CkAttributeId>, CkAttributeGraph>();
        _associationRoles = new Dictionary<CkId<CkAssociationRoleId>, CkAssociationRoleGraph>();
        Types = new ReadOnlyDictionary<CkId<CkTypeId>, CkTypeGraph>(_entities);
        Attributes = new ReadOnlyDictionary<CkId<CkAttributeId>, CkAttributeGraph>(_attributes);
        AssociationRoles = new ReadOnlyDictionary<CkId<CkAssociationRoleId>, CkAssociationRoleGraph>(_associationRoles);
    }

    public IReadOnlyDictionary<CkId<CkTypeId>, CkTypeGraph> Types { get; }
    public IReadOnlyDictionary<CkId<CkAttributeId>, CkAttributeGraph> Attributes { get; }
    public IReadOnlyDictionary<CkId<CkAssociationRoleId>, CkAssociationRoleGraph> AssociationRoles { get; }

    public CkAttributeGraph GetOrCreateAttribute(CkId<CkAttributeId> ckAttributeId, CkAttributeDto ckAttributeDto)
    {
        if (_attributes.TryGetValue(ckAttributeId, out var ckAttributeGraph))
        {
            return ckAttributeGraph;
        }
        
        ckAttributeGraph = new(ckAttributeId, ckAttributeDto);
        _attributes.Add(ckAttributeId, ckAttributeGraph);
        return ckAttributeGraph;
    }

    public CkTypeGraph GetOrCreateType(CkId<CkTypeId> ckTypeId, CkTypeDto ckTypeDto)
    {
        if (_entities.TryGetValue(ckTypeId, out var ckTypeGraph))
        {
            return ckTypeGraph;
        }
        
        ckTypeGraph = new(ckTypeId, ckTypeDto.IsAbstract, ckTypeDto.IsFinal);
        _entities.Add(ckTypeId, ckTypeGraph);
        return ckTypeGraph;
    }

    public CkAssociationRoleGraph GetOrCreateAssociationRoles(CkId<CkAssociationRoleId> ckAssociationId, CkAssociationRoleDto ckAssociationRole)
    {
        if (_associationRoles.TryGetValue(ckAssociationId, out var ckAssociationRoleGraph))
        {
            return ckAssociationRoleGraph;
        }
        
        ckAssociationRoleGraph = new(ckAssociationId, ckAssociationRole);
        _associationRoles.Add(ckAssociationId, ckAssociationRoleGraph);
        return ckAssociationRoleGraph;
    }
}