using System.Collections.ObjectModel;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
/// Defines the graph of a CK model
/// </summary>
public class CkModelGraph
{
    private readonly IDictionary<CkId<CkTypeId>, CkTypeGraph> _types;
    private readonly IDictionary<CkId<CkAttributeId>, CkAttributeGraph> _attributes;
    private readonly IDictionary<CkId<CkAssociationRoleId>, CkAssociationRoleGraph> _associationRoles;
    private readonly IDictionary<CkId<CkRecordId>, CkRecordGraph> _records;
    private readonly IDictionary<CkId<CkEnumId>, CkEnumGraph> _enums;

    /// <summary>
    /// Creates a new instance of <see cref="CkModelGraph"/>.
    /// </summary>
    public CkModelGraph()
    {
        _types = new Dictionary<CkId<CkTypeId>, CkTypeGraph>();
        _attributes = new Dictionary<CkId<CkAttributeId>, CkAttributeGraph>();
        _associationRoles = new Dictionary<CkId<CkAssociationRoleId>, CkAssociationRoleGraph>();
        _records = new Dictionary<CkId<CkRecordId>, CkRecordGraph>();
        _enums = new Dictionary<CkId<CkEnumId>, CkEnumGraph>();
        Types = new ReadOnlyDictionary<CkId<CkTypeId>, CkTypeGraph>(_types);
        Attributes = new ReadOnlyDictionary<CkId<CkAttributeId>, CkAttributeGraph>(_attributes);
        AssociationRoles = new ReadOnlyDictionary<CkId<CkAssociationRoleId>, CkAssociationRoleGraph>(_associationRoles);
        Records = new ReadOnlyDictionary<CkId<CkRecordId>, CkRecordGraph>(_records);
        Enums = new ReadOnlyDictionary<CkId<CkEnumId>, CkEnumGraph>(_enums);
    }

    /// <summary>
    /// Creates a new instance of <see cref="CkModelGraph"/>.
    /// </summary>
    /// <param name="ckCacheRoot">A cache root object from deserialization</param>
    public CkModelGraph(CkCacheRoot ckCacheRoot)
    {
        _types = ckCacheRoot.Types.ToDictionary(k => k.CkTypeId, v=> v);
        _attributes = ckCacheRoot.Attributes.ToDictionary(k => k.CkAttributeId, v=> v);
        _associationRoles = ckCacheRoot.AssociationRoles.ToDictionary(k => k.CkRoleId, v=> v);
        _records = ckCacheRoot.Records.ToDictionary(k => k.CkRecordId, v=> v);
        _enums = ckCacheRoot.Enums.ToDictionary(k => k.CkEnumId, v=> v);
        Types = new ReadOnlyDictionary<CkId<CkTypeId>, CkTypeGraph>(_types);
        Attributes = new ReadOnlyDictionary<CkId<CkAttributeId>, CkAttributeGraph>(_attributes);
        AssociationRoles = new ReadOnlyDictionary<CkId<CkAssociationRoleId>, CkAssociationRoleGraph>(_associationRoles);
        Records = new ReadOnlyDictionary<CkId<CkRecordId>, CkRecordGraph>(_records);
        Enums = new ReadOnlyDictionary<CkId<CkEnumId>, CkEnumGraph>(_enums);
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
    /// Returns the records of the model.
    /// </summary>
    public IReadOnlyDictionary<CkId<CkRecordId>, CkRecordGraph> Records { get; }
    
    /// <summary>
    /// Returns the enums of the model.
    /// </summary>
    public IReadOnlyDictionary<CkId<CkEnumId>, CkEnumGraph> Enums { get; }
    
    /// <summary>
    /// Returns the root object of the compiled version of a CK model.
    /// </summary>
    /// <returns></returns>
    public CkCacheRoot ToCkCacheRoot()
    {
        return new()
        {
            Types = _types.Values.ToList(),
            Attributes = _attributes.Values.ToList(),
            AssociationRoles = _associationRoles.Values.ToList(),
            Records = _records.Values.ToList(),
            Enums = _enums.Values.ToList()
        };
    }

    /// <summary>
    /// Gets or creates a new attribute.
    /// </summary>
    /// <param name="ckAttributeId"></param>
    /// <param name="ckAttributeDto"></param>
    /// <returns></returns>
    internal CkAttributeGraph GetOrCreateAttribute(CkId<CkAttributeId> ckAttributeId, CkAttributeDto ckAttributeDto)
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
    internal CkTypeGraph GetOrCreateType(CkId<CkTypeId> ckTypeId, CkTypeDto ckTypeDto)
    {
        if (_types.TryGetValue(ckTypeId, out var ckTypeGraph))
        {
            return ckTypeGraph;
        }
        
        ckTypeGraph = new(ckTypeId, ckTypeDto.IsAbstract, ckTypeDto.IsFinal);
        _types.Add(ckTypeId, ckTypeGraph);
        return ckTypeGraph;
    }

    /// <summary>
    /// Gets or creates a new association role.
    /// </summary>
    /// <param name="ckAssociationId"></param>
    /// <param name="ckAssociationRole"></param>
    /// <returns></returns>
    internal CkAssociationRoleGraph GetOrCreateAssociationRoles(CkId<CkAssociationRoleId> ckAssociationId, CkAssociationRoleDto ckAssociationRole)
    {
        if (_associationRoles.TryGetValue(ckAssociationId, out var ckAssociationRoleGraph))
        {
            return ckAssociationRoleGraph;
        }
        
        ckAssociationRoleGraph = new(ckAssociationId, ckAssociationRole);
        _associationRoles.Add(ckAssociationId, ckAssociationRoleGraph);
        return ckAssociationRoleGraph;
    }
    
    /// <summary>
    /// Gets or creates a new record.
    /// </summary>
    /// <param name="ckRecordId"></param>
    /// <param name="ckRecordDto"></param>
    /// <returns></returns>
    internal CkRecordGraph GetOrCreateRecord(CkId<CkRecordId> ckRecordId, CkRecordDto ckRecordDto)
    {
        if (_records.TryGetValue(ckRecordId, out var ckRecordGraph))
        {
            return ckRecordGraph;
        }
        
        ckRecordGraph = new(ckRecordId, ckRecordDto.IsAbstract, ckRecordDto.IsFinal);
        _records.Add(ckRecordId, ckRecordGraph);
        return ckRecordGraph;
    }
    
    /// <summary>
    /// Gets or creates a new enum.
    /// </summary>
    /// <param name="ckEnumId"></param>
    /// <param name="ckEnumDto"></param>
    /// <returns></returns>
    internal CkEnumGraph GetOrCreateEnum(CkId<CkEnumId> ckEnumId, CkEnumDto ckEnumDto)
    {
        if (_enums.TryGetValue(ckEnumId, out var ckEnumGraph))
        {
            return ckEnumGraph;
        }
        
        ckEnumGraph = new(ckEnumId, ckEnumDto);
        _enums.Add(ckEnumId, ckEnumGraph);
        return ckEnumGraph;
    }
}