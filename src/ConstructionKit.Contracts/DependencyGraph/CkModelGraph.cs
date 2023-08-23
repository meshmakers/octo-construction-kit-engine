using System.Collections.ObjectModel;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
/// Defines the graph of a CK model
/// </summary>
public class CkModelGraph
{
    private readonly IDictionary<CkId<CkTypeId>, CkTypeGraph> _entities;
    private readonly IDictionary<CkId<CkAttributeId>, CkAttributeGraph> _attributes;
    private readonly IDictionary<CkId<CkAssociationRoleId>, CkAssociationRoleGraph> _associationRoles;

    /// <summary>
    /// Creates a new instance of <see cref="CkModelGraph"/>.
    /// </summary>
    public CkModelGraph()
    {
        _entities = new Dictionary<CkId<CkTypeId>, CkTypeGraph>();
        _attributes = new Dictionary<CkId<CkAttributeId>, CkAttributeGraph>();
        _associationRoles = new Dictionary<CkId<CkAssociationRoleId>, CkAssociationRoleGraph>();
        Types = new ReadOnlyDictionary<CkId<CkTypeId>, CkTypeGraph>(_entities);
        Attributes = new ReadOnlyDictionary<CkId<CkAttributeId>, CkAttributeGraph>(_attributes);
        AssociationRoles = new ReadOnlyDictionary<CkId<CkAssociationRoleId>, CkAssociationRoleGraph>(_associationRoles);
    }

    /// <summary>
    /// Returns the types of the model.
    /// </summary>
    public IReadOnlyDictionary<CkId<CkTypeId>, CkTypeGraph> Types { get; }
    
    /// <summary>
    /// Returns the attributes of the model.
    /// </summary>
    public IReadOnlyDictionary<CkId<CkAttributeId>, CkAttributeGraph> Attributes { get; }
    
    /// <summary>
    /// Returns the association roles of the model.
    /// </summary>
    public IReadOnlyDictionary<CkId<CkAssociationRoleId>, CkAssociationRoleGraph> AssociationRoles { get; }

    /// <summary>
    /// Gets or creates a new attribute.
    /// </summary>
    /// <param name="ckAttributeId"></param>
    /// <param name="ckAttributeDto"></param>
    /// <returns></returns>
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

    /// <summary>
    /// Gets or creates a new type.
    /// </summary>
    /// <param name="ckTypeId"></param>
    /// <param name="ckTypeDto"></param>
    /// <returns></returns>
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

    /// <summary>
    /// Gets or creates a new association role.
    /// </summary>
    /// <param name="ckAssociationId"></param>
    /// <param name="ckAssociationRole"></param>
    /// <returns></returns>
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