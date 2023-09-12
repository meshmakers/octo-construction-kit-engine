using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
/// Represents a construction kit type in the dependency graph
/// </summary>
[DebuggerDisplay("{" + nameof(Path) + "}")]
public class CkTypeGraph
{
    private readonly List<CkGraphTypeInheritance> _baseTypes;
    private readonly List<CkGraphTypeInheritance> _derivedTypes;
    private readonly Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph> _allAttributes;
    private readonly Dictionary<string, CkTypeAttributeGraph> _allAttributesByName;

    /// <summary>
    /// Creates a new instance of <see cref="CkTypeGraph"/>.
    /// </summary>
    /// <param name="ckTypeId"></param>
    /// <param name="ckTypeDto"></param>
    public CkTypeGraph(CkId<CkTypeId> ckTypeId, CkTypeDto ckTypeDto)
    {
        CkTypeId = ckTypeId;
        IsAbstract = ckTypeDto.IsAbstract;
        IsFinal = ckTypeDto.IsFinal;
        DerivedFromCkTypeId = ckTypeDto.DerivedFromCkTypeId;
        _baseTypes = new List<CkGraphTypeInheritance>();
        _derivedTypes = new List<CkGraphTypeInheritance>();
        _allAttributes = new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>();
        _allAttributesByName = new Dictionary<string, CkTypeAttributeGraph>();
        BaseTypes = new ReadOnlyCollection<CkGraphTypeInheritance>(_baseTypes);
        DerivedTypes = new ReadOnlyCollection<CkGraphTypeInheritance>(_derivedTypes);
        Associations = new(ckTypeDto.Associations ?? new List<CkTypeAssociationDto>());
        DefinedAttributes = new ReadOnlyCollection<CkTypeAttributeDto>(ckTypeDto.Attributes ?? new List<CkTypeAttributeDto>());
        AllAttributes = new ReadOnlyDictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>(_allAttributes);
        AllAttributesByName = new ReadOnlyDictionary<string, CkTypeAttributeGraph>(_allAttributesByName);
        Indexes = new Collection<CkTypeIndexDto>(ckTypeDto.Indexes ?? new List<CkTypeIndexDto>());
    }

    /// <summary>
    /// Creates a new instance of <see cref="CkTypeGraph"/>.
    /// </summary>
    /// <param name="ckTypeId"></param>
    /// <param name="isAbstract"></param>
    /// <param name="isFinal"></param>
    /// <param name="baseTypes"></param>
    /// <param name="derivedFromCkTypeId"></param>
    /// <param name="derivedTypes"></param>
    /// <param name="definedAttributes"></param>
    /// <param name="allAttributes"></param>
    /// <param name="indexes"></param>
    /// <param name="associations"></param>
    [JsonConstructor]
    public CkTypeGraph(CkId<CkTypeId> ckTypeId, bool isAbstract, bool isFinal,
        IReadOnlyCollection<CkGraphTypeInheritance> baseTypes,
        CkId<CkTypeId>? derivedFromCkTypeId,
        IReadOnlyCollection<CkGraphTypeInheritance> derivedTypes,
        IReadOnlyCollection<CkTypeAttributeDto> definedAttributes,
        IReadOnlyDictionary<CkId<CkAttributeId>, CkTypeAttributeGraph> allAttributes,
        IReadOnlyCollection<CkTypeIndexDto> indexes, CkGraphDirectedAssociations associations)
    {
        CkTypeId = ckTypeId;
        IsAbstract = isAbstract;
        IsFinal = isFinal;
        DerivedFromCkTypeId = derivedFromCkTypeId;

        _baseTypes = new List<CkGraphTypeInheritance>(baseTypes);
        _derivedTypes = new List<CkGraphTypeInheritance>(derivedTypes);
        _allAttributes = new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>(allAttributes
            .ToDictionary(k => k.Key, v => v.Value));
        _allAttributesByName = new Dictionary<string, CkTypeAttributeGraph>(allAttributes
            .ToDictionary(k => k.Value.AttributeName, v => v.Value));
        BaseTypes = new ReadOnlyCollection<CkGraphTypeInheritance>(_baseTypes);
        DerivedTypes = new ReadOnlyCollection<CkGraphTypeInheritance>(_derivedTypes);
        Associations = associations;
        DefinedAttributes = new List<CkTypeAttributeDto>(definedAttributes);
        AllAttributes = new ReadOnlyDictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>(_allAttributes);
        AllAttributesByName = new ReadOnlyDictionary<string, CkTypeAttributeGraph>(_allAttributesByName);
        Indexes = new List<CkTypeIndexDto>(indexes);
    }


    /// <summary>
    /// Defines the base type of this type. Only one type may not have a base type: System/Entity
    /// </summary>
    public CkId<CkTypeId>? DerivedFromCkTypeId { get; }

    /// <summary>
    ///     Gets or sets the construction kit id
    /// </summary>
    public CkId<CkTypeId> CkTypeId { get; }

    /// <summary>
    ///     If true, the type cannot be inherited again
    /// </summary>
    public bool IsFinal { get; }

    /// <summary>
    ///     If true, the type cannot be instantiated by a runtime entity
    /// </summary>
    public bool IsAbstract { get; }

    /// <summary>
    /// Returns a list of base types of the given construction kit type
    /// </summary>
    public IReadOnlyCollection<CkGraphTypeInheritance> BaseTypes { get; }

    /// <summary>
    /// Returns a list of derived types of the given construction kit type
    /// </summary>
    public IReadOnlyCollection<CkGraphTypeInheritance> DerivedTypes { get; }

    /// <summary>
    /// Returns a list of associations including inherited ones.
    /// </summary>
    public CkGraphDirectedAssociations Associations { get; }

    /// <summary>
    /// Returns a list of indexes including inherited ones.
    /// </summary>
    public IReadOnlyCollection<CkTypeIndexDto> Indexes { get; set; }

    /// <summary>
    ///     Gets or sets a list of attributes that are defined by the current type
    /// </summary>
    public IReadOnlyCollection<CkTypeAttributeDto> DefinedAttributes { get; }

    /// <summary>
    ///     Gets or sets a list of attributes including inherited ones.
    /// </summary>
    public IReadOnlyDictionary<CkId<CkAttributeId>, CkTypeAttributeGraph> AllAttributes { get; }
    
    /// <summary>
    ///     Gets or sets a list of attributes including inherited ones.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, CkTypeAttributeGraph> AllAttributesByName { get; }

    /// <summary>
    /// Returns a string that describes the inheritance chain
    /// </summary>
    [JsonIgnore]
    public string Path => CkTypeId + ": " + string.Join("->", BaseTypes.Select(x => x.BaseCkTypeId));

    /// <summary>
    /// Adds a list of base types of the current type
    /// </summary>
    /// <param name="baseTypeList"></param>
    internal void AddBaseTypes(IEnumerable<CkGraphTypeInheritance> baseTypeList)
    {
        _baseTypes.AddRange(baseTypeList);
    }

    /// <summary>
    /// Adds a derived types of the current type
    /// </summary>
    /// <param name="ckGraphTypeInheritance"></param>
    internal void AddDerivedTypes(CkGraphTypeInheritance ckGraphTypeInheritance)
    {
        _derivedTypes.Add(ckGraphTypeInheritance);
    }

    /// <summary>
    /// Adds a attribute to the current type
    /// </summary>
    /// <param name="ckTypeAttributeGraph"></param>
    internal bool TryAddAttribute(CkTypeAttributeGraph ckTypeAttributeGraph)
    {
        if (_allAttributes.ContainsKey(ckTypeAttributeGraph.CkAttributeId))
        {
            return false;
        }

        _allAttributes.Add(ckTypeAttributeGraph.CkAttributeId, ckTypeAttributeGraph);
        _allAttributesByName[ckTypeAttributeGraph.AttributeName] = ckTypeAttributeGraph;
        return true;
    }

    /// <summary>
    /// Returns a list of derived types of the given construction kit type
    /// </summary>
    /// <param name="includeSelf">When true, the current type is included to the list</param>
    /// <returns></returns>
    public IReadOnlyCollection<CkId<CkTypeId>> GetAllDerivedTypes(bool includeSelf)
    {
        var list = new List<CkId<CkTypeId>>();
        if (includeSelf)
        {
            list.Add(CkTypeId);
        }

        list.AddRange(_derivedTypes.Select(x => x.InheritorCkTypeId));

        return list;
    }

    /// <summary>
    /// Returns a list of base types of the given construction kit type
    /// </summary>
    /// <param name="includeSelf">When true, the current type is included to the list</param>
    /// <returns></returns>
    public IReadOnlyCollection<CkId<CkTypeId>> GetBaseTypes(bool includeSelf)
    {
        var list = new List<CkId<CkTypeId>>();
        if (includeSelf)
        {
            list.Add(CkTypeId);
        }

        list.AddRange(_baseTypes.Select(x => x.BaseCkTypeId));

        return list;
    }
}