using System.Collections.ObjectModel;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;

/// <summary>
///     Defines the graph of a CK model
/// </summary>
public class CkModelGraph : ICkModelGraph
{
    private readonly Dictionary<CkModelId, CkModelPropertiesDto> _models;
    private readonly Dictionary<CkModelId, ICollection<CkModelId>> _dependencies;

    private readonly Dictionary<CkId<CkAssociationRoleId>, CkAssociationRoleGraph> _associationRoles;
    private readonly Dictionary<CkId<CkAttributeId>, CkAttributeGraph> _attributes;
    private readonly Dictionary<CkId<CkEnumId>, CkEnumGraph> _enums;
    private readonly Dictionary<CkId<CkRecordId>, CkRecordGraph> _records;
    private readonly Dictionary<CkId<CkTypeId>, CkTypeGraph> _types;

    private readonly Dictionary<RtCkId<CkTypeId>, CkTypeGraph> _typesByRtCk;
    private readonly Dictionary<RtCkId<CkAttributeId>, CkAttributeGraph> _attributesByRtCk;
    private readonly Dictionary<RtCkId<CkAssociationRoleId>, CkAssociationRoleGraph> _associationRolesByRtCk;
    private readonly Dictionary<RtCkId<CkRecordId>, CkRecordGraph> _recordsByRtCk;
    private readonly Dictionary<RtCkId<CkEnumId>, CkEnumGraph> _enumsByRtCk;

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
        _models = new Dictionary<CkModelId, CkModelPropertiesDto>();

        _typesByRtCk = new Dictionary<RtCkId<CkTypeId>, CkTypeGraph>();
        _attributesByRtCk = new Dictionary<RtCkId<CkAttributeId>, CkAttributeGraph>();
        _associationRolesByRtCk = new Dictionary<RtCkId<CkAssociationRoleId>, CkAssociationRoleGraph>();
        _recordsByRtCk = new Dictionary<RtCkId<CkRecordId>, CkRecordGraph>();
        _enumsByRtCk = new Dictionary<RtCkId<CkEnumId>, CkEnumGraph>();
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
        _models = ckCacheRoot.Models.ToDictionary(k => k.ModelId, v => v);

        _typesByRtCk =
            new Dictionary<RtCkId<CkTypeId>, CkTypeGraph>(
                _types.Values.ToDictionary(k => k.CkTypeId.ToRtCkId(), v => v));
        _attributesByRtCk =
            new Dictionary<RtCkId<CkAttributeId>, CkAttributeGraph>(
                _attributes.Values.ToDictionary(k => k.CkAttributeId.ToRtCkId(), v => v));
        _associationRolesByRtCk =
            new Dictionary<RtCkId<CkAssociationRoleId>, CkAssociationRoleGraph>(
                _associationRoles.Values.ToDictionary(k => k.CkRoleId.ToRtCkId(), v => v));
        _recordsByRtCk =
            new Dictionary<RtCkId<CkRecordId>, CkRecordGraph>(
                _records.Values.ToDictionary(k => k.CkRecordId.ToRtCkId(), v => v));
        _enumsByRtCk =
            new Dictionary<RtCkId<CkEnumId>, CkEnumGraph>(
                _enums.Values.ToDictionary(k => k.CkEnumId.ToRtCkId(), v => v));
    }

    /// <summary>
    ///     Returns the types of the model.
    /// </summary>
    public IReadOnlyDictionary<CkId<CkTypeId>, CkTypeGraph> Types => _types;

    /// <summary>
    ///     Returns the attributes of the model.
    /// </summary>
    public IReadOnlyDictionary<CkId<CkAttributeId>, CkAttributeGraph> Attributes => _attributes;

    /// <summary>
    ///     Returns the association roles of the model.
    /// </summary>
    public IReadOnlyDictionary<CkId<CkAssociationRoleId>, CkAssociationRoleGraph> AssociationRoles =>
        _associationRoles;

    /// <summary>
    ///     Returns the records of the model.
    /// </summary>
    public IReadOnlyDictionary<CkId<CkRecordId>, CkRecordGraph> Records => _records;

    /// <summary>
    ///     Returns the enums of the model.
    /// </summary>
    public IReadOnlyDictionary<CkId<CkEnumId>, CkEnumGraph> Enums => _enums;

    /// <inheritdoc />
    public IReadOnlyDictionary<RtCkId<CkTypeId>, CkTypeGraph> TypesByRtCk => _typesByRtCk;

    /// <inheritdoc />
    public IReadOnlyDictionary<RtCkId<CkAttributeId>, CkAttributeGraph> AttributesByRtCk =>
        _attributesByRtCk;

    /// <inheritdoc />
    public IReadOnlyDictionary<RtCkId<CkAssociationRoleId>, CkAssociationRoleGraph> AssociationRolesByRtCk =>
        _associationRolesByRtCk;

    /// <inheritdoc />
    public IReadOnlyDictionary<RtCkId<CkRecordId>, CkRecordGraph> RecordsByRtCk => _recordsByRtCk;

    /// <inheritdoc />
    public IReadOnlyDictionary<RtCkId<CkEnumId>, CkEnumGraph> EnumsByRtCk => _enumsByRtCk;

    /// <summary>
    ///     Returns a list of model dependencies.
    /// </summary>
    public IReadOnlyDictionary<CkModelId, ICollection<CkModelId>> Dependencies => _dependencies;

    /// <summary>
    ///     Returns a list of model dependencies.
    /// </summary>
    public IReadOnlyDictionary<CkModelId, CkModelPropertiesDto> Models => _models;

    /// <summary>
    ///     Returns the root object of the compiled version of a CK model.
    /// </summary>
    /// <returns></returns>
    public CkCacheRoot ToCkCacheRoot()
    {
        return new CkCacheRoot
        {
            Models = _models.OrderBy(x => x.Key).Select(x => x.Value).ToList(),
            Dependencies = _dependencies.ToDictionary(x => x.Key, x => x.Value),
            Types = _types.Values.OrderBy(x => x.CkTypeId).ToList(),
            Attributes = _attributes.Values.OrderBy(x => x.CkAttributeId).ToList(),
            AssociationRoles = _associationRoles.Values.OrderBy(x => x.CkRoleId).ToList(),
            Records = _records.Values.OrderBy(x => x.CkRecordId).ToList(),
            Enums = _enums.Values.OrderBy(x => x.CkEnumId).ToList()
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
        _attributesByRtCk.Add(ckAttributeId.ToRtCkId(), ckAttributeGraph);
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
        _typesByRtCk.Add(ckTypeId.ToRtCkId(), ckTypeGraph);

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
        _associationRolesByRtCk.Add(ckAssociationId.ToRtCkId(), ckAssociationRoleGraph);
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
        _recordsByRtCk.Add(ckRecordId.ToRtCkId(), ckRecordGraph);
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
        _enumsByRtCk.Add(ckEnumId.ToRtCkId(), ckEnumGraph);
        return ckEnumGraph;
    }

    /// <summary>
    /// Gets or creates a new model.
    /// </summary>
    /// <param name="ckModelId"></param>
    /// <param name="description"></param>
    /// <returns></returns>
    public CkModelPropertiesDto GetOrCreateModel(CkModelId ckModelId, string? description)
    {
        if (_models.TryGetValue(ckModelId, out var ckModelPropertiesDto))
        {
            return ckModelPropertiesDto;
        }

        ckModelPropertiesDto = new CkModelPropertiesDto
        {
            ModelId = ckModelId,
            Description = description
        };
        _models.Add(ckModelId, ckModelPropertiesDto);
        return ckModelPropertiesDto;
    }

    /// <summary>
    ///     Appends the model elements of the given <paramref name="ckCompiledModelRoot" /> to this instance.
    /// </summary>
    /// <param name="ckCompiledModelRoot">The compiled model root to append</param>
    public void AppendModel(CkCompiledModelRoot ckCompiledModelRoot)
    {
        _dependencies.Add(ckCompiledModelRoot.ModelId, ckCompiledModelRoot.Dependencies ?? []);
        GetOrCreateModel(ckCompiledModelRoot.ModelId, ckCompiledModelRoot.Description);

        if (ckCompiledModelRoot.Attributes != null)
        {
            foreach (var ckAttribute in ckCompiledModelRoot.Attributes)
            {
                GetOrCreateAttribute(new CkId<CkAttributeId>(ckCompiledModelRoot.ModelId, ckAttribute.AttributeId),
                    ckAttribute);
            }
        }

        if (ckCompiledModelRoot.AssociationRoles != null)
        {
            foreach (var ckAssociationRole in ckCompiledModelRoot.AssociationRoles)
            {
                GetOrCreateAssociationRole(
                    new CkId<CkAssociationRoleId>(ckCompiledModelRoot.ModelId, ckAssociationRole.AssociationRoleId),
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

    /// <summary>
    /// Gets all Types of a given ckModelGraph
    /// </summary>
    /// <returns></returns>
    public IEnumerable<CkTypeGraph> GetTypes()
    {
        return Types.Select(x => x.Value);
    }

    /// <summary>
    /// Gets all Attributes of a given ckModelGraph
    /// </summary>
    /// <returns></returns>
    public IEnumerable<CkAttributeGraph> GetAttributes()
    {
        return Attributes.Select(x => x.Value);
    }

    /// <summary>
    /// Gets all Enums of a given ckModelGraph
    /// </summary>
    /// <returns></returns>
    public IEnumerable<CkEnumGraph> GetEnums()
    {
        return Enums.Select(x => x.Value);
    }

    /// <summary>
    /// Gets all Records of a given ckModelGraph
    /// </summary>
    /// <returns></returns>
    public IEnumerable<CkRecordGraph> GetRecords()
    {
        return Records.Select(x => x.Value);
    }

    /// <summary>
    /// Gets all AssociationRoles of a given ckModelGraph
    /// </summary>
    /// <returns></returns>
    public IEnumerable<CkAssociationRoleGraph> GetAssociationRoles()
    {
        return AssociationRoles.Select(x => x.Value);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<CkTypeQueryColumn> GetCkTypeQueryColumnPaths(CkId<CkTypeId> ckTypeId, bool ignoreNavigationProperties)
    {
        var collector = new CkTypeQueryColumnCollector(this);
        return collector.GetColumns(ckTypeId, ignoreNavigationProperties);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<CkTypeQueryColumn> GetCkTypeQueryColumnPathsByRtCkId(RtCkId<CkTypeId> rtCkTypeId, bool ignoreNavigationProperties)
    {
        var collector = new CkTypeQueryColumnCollector(this);
        return collector.GetColumnsByRtCkId(rtCkTypeId, ignoreNavigationProperties);
    }
}