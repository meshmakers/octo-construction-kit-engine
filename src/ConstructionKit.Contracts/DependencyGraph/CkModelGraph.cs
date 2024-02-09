using System.Collections.ObjectModel;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
///     Defines the graph of a CK model
/// </summary>
public class CkModelGraph
{
    private readonly IDictionary<CkId<CkAssociationRoleId>, CkAssociationRoleGraph> _associationRoles;
    private readonly IDictionary<CkId<CkAttributeId>, CkAttributeGraph> _attributes;
    private readonly IDictionary<CkModelId, ICollection<CkModelId>> _dependencies;
    private readonly IDictionary<CkId<CkEnumId>, CkEnumGraph> _enums;
    private readonly IDictionary<CkId<CkRecordId>, CkRecordGraph> _records;
    private readonly IDictionary<CkId<CkTypeId>, CkTypeGraph> _types;

    /// <summary>
    ///     Creates a new instance of <see cref="CkModelGraph" />.
    /// </summary>
    public CkModelGraph()
    {
        _types = new Dictionary<CkId<CkTypeId>, CkTypeGraph>();
        _attributes = new Dictionary<CkId<CkAttributeId>, CkAttributeGraph>();
        _associationRoles = new Dictionary<CkId<CkAssociationRoleId>, CkAssociationRoleGraph>();
        _records = new Dictionary<CkId<CkRecordId>, CkRecordGraph>();
        _enums = new Dictionary<CkId<CkEnumId>, CkEnumGraph>();
        _dependencies = new Dictionary<CkModelId, ICollection<CkModelId>>();
        Types = new ReadOnlyDictionary<CkId<CkTypeId>, CkTypeGraph>(_types);
        Attributes = new ReadOnlyDictionary<CkId<CkAttributeId>, CkAttributeGraph>(_attributes);
        AssociationRoles = new ReadOnlyDictionary<CkId<CkAssociationRoleId>, CkAssociationRoleGraph>(_associationRoles);
        Records = new ReadOnlyDictionary<CkId<CkRecordId>, CkRecordGraph>(_records);
        Enums = new ReadOnlyDictionary<CkId<CkEnumId>, CkEnumGraph>(_enums);
        Dependencies = new ReadOnlyDictionary<CkModelId, ICollection<CkModelId>>(_dependencies);
    }

    /// <summary>
    ///     Creates a new instance of <see cref="CkModelGraph" />.
    /// </summary>
    /// <param name="ckCacheRoot">A cache root object from deserialization</param>
    public CkModelGraph(CkCacheRoot ckCacheRoot)
    {
        _types = ckCacheRoot.Types.ToDictionary(k => k.CkTypeId, v => v);
        _attributes = ckCacheRoot.Attributes.ToDictionary(k => k.CkAttributeId, v => v);
        _associationRoles = ckCacheRoot.AssociationRoles.ToDictionary(k => k.CkRoleId, v => v);
        _records = ckCacheRoot.Records.ToDictionary(k => k.CkRecordId, v => v);
        _enums = ckCacheRoot.Enums.ToDictionary(k => k.CkEnumId, v => v);
        _dependencies = ckCacheRoot.Dependencies.ToDictionary(k => k.Key, v => v.Value);
        Types = new ReadOnlyDictionary<CkId<CkTypeId>, CkTypeGraph>(_types);
        Attributes = new ReadOnlyDictionary<CkId<CkAttributeId>, CkAttributeGraph>(_attributes);
        AssociationRoles = new ReadOnlyDictionary<CkId<CkAssociationRoleId>, CkAssociationRoleGraph>(_associationRoles);
        Records = new ReadOnlyDictionary<CkId<CkRecordId>, CkRecordGraph>(_records);
        Enums = new ReadOnlyDictionary<CkId<CkEnumId>, CkEnumGraph>(_enums);
        Dependencies = new ReadOnlyDictionary<CkModelId, ICollection<CkModelId>>(_dependencies);
    }

    /// <summary>
    ///     Returns the types of the model.
    /// </summary>
    public IReadOnlyDictionary<CkId<CkTypeId>, CkTypeGraph> Types { get; }

    /// <summary>
    ///     Returns the attributes of the model.
    /// </summary>
    public IReadOnlyDictionary<CkId<CkAttributeId>, CkAttributeGraph> Attributes { get; }

    /// <summary>
    ///     Returns the association roles of the model.
    /// </summary>
    public IReadOnlyDictionary<CkId<CkAssociationRoleId>, CkAssociationRoleGraph> AssociationRoles { get; }

    /// <summary>
    ///     Returns the records of the model.
    /// </summary>
    public IReadOnlyDictionary<CkId<CkRecordId>, CkRecordGraph> Records { get; }

    /// <summary>
    ///     Returns the enums of the model.
    /// </summary>
    public IReadOnlyDictionary<CkId<CkEnumId>, CkEnumGraph> Enums { get; }

    /// <summary>
    ///     Returns a list of model dependencies.
    /// </summary>
    public IReadOnlyDictionary<CkModelId, ICollection<CkModelId>> Dependencies { get; }

    /// <summary>
    ///     Returns the root object of the compiled version of a CK model.
    /// </summary>
    /// <returns></returns>
    public CkCacheRoot ToCkCacheRoot()
    {
        return new CkCacheRoot
        {
            Types = _types.Values.OrderBy(x=> x.CkTypeId).ToList(),
            Attributes = _attributes.Values.OrderBy(x=> x.CkAttributeId).ToList(),
            AssociationRoles = _associationRoles.Values.OrderBy(x=> x.CkRoleId).ToList(),
            Records = _records.Values.OrderBy(x=> x.CkRecordId).ToList(),
            Enums = _enums.Values.OrderBy(x=> x.CkEnumId).ToList()
        };
    }

    /// <summary>
    ///     Gets or creates a new attribute.
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

        ckAttributeGraph = new CkAttributeGraph(ckAttributeId, ckAttributeDto);
        _attributes.Add(ckAttributeId, ckAttributeGraph);
        return ckAttributeGraph;
    }

    /// <summary>
    ///     Gets or creates a new type.
    /// </summary>
    /// <param name="ckTypeId"></param>
    /// <param name="ckTypeDto"></param>
    /// <returns></returns>
    public CkTypeGraph GetOrCreateType(CkId<CkTypeId> ckTypeId, CkCompiledTypeDto ckTypeDto)
    {
        if (_types.TryGetValue(ckTypeId, out var ckTypeGraph))
        {
            return ckTypeGraph;
        }

        ckTypeGraph = new CkTypeGraph(ckTypeId, ckTypeDto);
        _types.Add(ckTypeId, ckTypeGraph);

        return ckTypeGraph;
    }

    /// <summary>
    ///     Gets or creates a new association role.
    /// </summary>
    /// <param name="ckAssociationId"></param>
    /// <param name="ckAssociationRole"></param>
    /// <returns></returns>
    public CkAssociationRoleGraph GetOrCreateAssociationRole(CkId<CkAssociationRoleId> ckAssociationId,
        CkAssociationRoleDto ckAssociationRole)
    {
        if (_associationRoles.TryGetValue(ckAssociationId, out var ckAssociationRoleGraph))
        {
            return ckAssociationRoleGraph;
        }

        ckAssociationRoleGraph = new CkAssociationRoleGraph(ckAssociationId, ckAssociationRole);
        _associationRoles.Add(ckAssociationId, ckAssociationRoleGraph);
        return ckAssociationRoleGraph;
    }

    /// <summary>
    ///     Gets or creates a new record.
    /// </summary>
    /// <param name="ckRecordId"></param>
    /// <param name="ckRecordDto"></param>
    /// <returns></returns>
    public CkRecordGraph GetOrCreateRecord(CkId<CkRecordId> ckRecordId, CkRecordDto ckRecordDto)
    {
        if (_records.TryGetValue(ckRecordId, out var ckRecordGraph))
        {
            return ckRecordGraph;
        }

        ckRecordGraph = new CkRecordGraph(ckRecordId, ckRecordDto);
        _records.Add(ckRecordId, ckRecordGraph);
        return ckRecordGraph;
    }

    /// <summary>
    ///     Gets or creates a new enum.
    /// </summary>
    /// <param name="ckEnumId"></param>
    /// <param name="ckEnumDto"></param>
    /// <returns></returns>
    public CkEnumGraph GetOrCreateEnum(CkId<CkEnumId> ckEnumId, CkEnumDto ckEnumDto)
    {
        if (_enums.TryGetValue(ckEnumId, out var ckEnumGraph))
        {
            return ckEnumGraph;
        }

        ckEnumGraph = new CkEnumGraph(ckEnumId, ckEnumDto);
        _enums.Add(ckEnumId, ckEnumGraph);
        return ckEnumGraph;
    }

    /// <summary>
    ///     Appends the model elements of the given <paramref name="ckCompiledModelRoot" /> to this instance.
    /// </summary>
    /// <param name="ckCompiledModelRoot">The compiled model root to append</param>
    public void AppendModel(CkCompiledModelRoot ckCompiledModelRoot)
    {
        _dependencies.Add(ckCompiledModelRoot.ModelId, ckCompiledModelRoot.Dependencies ?? new List<CkModelId>());

        if (ckCompiledModelRoot.Attributes != null)
        {
            foreach (var ckAttribute in ckCompiledModelRoot.Attributes)
            {
                GetOrCreateAttribute(new CkId<CkAttributeId>(ckCompiledModelRoot.ModelId, ckAttribute.AttributeId), ckAttribute);
            }
        }

        if (ckCompiledModelRoot.AssociationRoles != null)
        {
            foreach (var ckAssociationRole in ckCompiledModelRoot.AssociationRoles)
            {
                GetOrCreateAssociationRole(new CkId<CkAssociationRoleId>(ckCompiledModelRoot.ModelId, ckAssociationRole.AssociationRoleId),
                    ckAssociationRole);
            }
        }

        if (ckCompiledModelRoot.Types != null)
        {
            foreach (var ckTypeDto in ckCompiledModelRoot.Types)
            {
                GetOrCreateType(new CkId<CkTypeId>(ckCompiledModelRoot.ModelId, ckTypeDto.TypeId), ckTypeDto);
            }
        }

        if (ckCompiledModelRoot.Records != null)
        {
            foreach (var ckRecordDto in ckCompiledModelRoot.Records)
            {
                GetOrCreateRecord(new CkId<CkRecordId>(ckCompiledModelRoot.ModelId, ckRecordDto.RecordId), ckRecordDto);
            }
        }

        if (ckCompiledModelRoot.Enums != null)
        {
            foreach (var ckEnumDto in ckCompiledModelRoot.Enums)
            {
                GetOrCreateEnum(new CkId<CkEnumId>(ckCompiledModelRoot.ModelId, ckEnumDto.EnumId), ckEnumDto);
            }
        }
    }
}