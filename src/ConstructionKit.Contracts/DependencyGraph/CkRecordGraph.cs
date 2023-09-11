using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
/// Represents a construction kit record in the dependency graph
/// </summary>
[DebuggerDisplay("{" + nameof(Path) + "}")]
public class CkRecordGraph
{
    private readonly List<CkGraphRecordInheritance> _baseRecords;
    private readonly List<CkGraphRecordInheritance> _derivedRecords;
    private readonly Dictionary<CkId<CkAttributeId>, CkTypeAttributeDto> _allAttributes;

    /// <summary>
    /// Creates a new instance of <see cref="CkRecordGraph"/>.
    /// </summary>
    /// <param name="ckRecordId"></param>
    /// <param name="ckRecordDto"></param>
    public CkRecordGraph(CkId<CkRecordId> ckRecordId, CkRecordDto ckRecordDto)
    {
        CkRecordId = ckRecordId;
        IsAbstract = ckRecordDto.IsAbstract;
        IsFinal = ckRecordDto.IsFinal;
        DerivedFromCkRecordId = ckRecordDto.DerivedFromCkRecordId;
        _baseRecords = new List<CkGraphRecordInheritance>();
        _derivedRecords = new List<CkGraphRecordInheritance>();
        _allAttributes = new Dictionary<CkId<CkAttributeId>, CkTypeAttributeDto>(ckRecordDto.Attributes?.ToDictionary(
            x => x.CkAttributeId) ?? new Dictionary<CkId<CkAttributeId>, CkTypeAttributeDto>());
        BaseRecords = new ReadOnlyCollection<CkGraphRecordInheritance>(_baseRecords);
        DerivedRecords = new ReadOnlyCollection<CkGraphRecordInheritance>(_derivedRecords);
        DefinedAttributes = new ReadOnlyCollection<CkTypeAttributeDto>(ckRecordDto.Attributes ?? new List<CkTypeAttributeDto>());
        AllAttributes = new ReadOnlyDictionary<CkId<CkAttributeId>, CkTypeAttributeDto>(_allAttributes);
    }

    /// <summary>
    /// Creates a new instance of <see cref="CkRecordGraph"/>.
    /// </summary>
    /// <param name="ckRecordId"></param>
    /// <param name="isAbstract"></param>
    /// <param name="isFinal"></param>
    /// <param name="baseRecords"></param>
    /// <param name="derivedFromCkRecordId"></param>
    /// <param name="derivedRecords"></param>
    /// <param name="definedAttributes"></param>
    /// <param name="allAttributes"></param>
    [JsonConstructor]
    public CkRecordGraph(CkId<CkRecordId> ckRecordId, bool isAbstract, bool isFinal, 
        IReadOnlyCollection<CkGraphRecordInheritance> baseRecords, 
        CkId<CkRecordId>? derivedFromCkRecordId,
        IReadOnlyCollection<CkGraphRecordInheritance> derivedRecords,
        IReadOnlyCollection<CkTypeAttributeDto> definedAttributes,
        IReadOnlyDictionary<CkId<CkAttributeId>, CkTypeAttributeDto> allAttributes)
    {
        CkRecordId = ckRecordId;
        IsAbstract = isAbstract;
        IsFinal = isFinal;
        DerivedFromCkRecordId = derivedFromCkRecordId;
        
        _baseRecords = new List<CkGraphRecordInheritance>(baseRecords);
        _derivedRecords = new List<CkGraphRecordInheritance>(derivedRecords);
        _allAttributes = new Dictionary<CkId<CkAttributeId>, CkTypeAttributeDto>(allAttributes
            .ToDictionary(k=> k.Key, v=> v.Value));
        BaseRecords = new ReadOnlyCollection<CkGraphRecordInheritance>(_baseRecords);
        DerivedRecords = new ReadOnlyCollection<CkGraphRecordInheritance>(_derivedRecords);
        DefinedAttributes = new List<CkTypeAttributeDto>(definedAttributes);
        AllAttributes = new ReadOnlyDictionary<CkId<CkAttributeId>, CkTypeAttributeDto>(_allAttributes);
    }

    /// <summary>
    /// Defines the base record of this record. 
    /// </summary>
    public CkId<CkRecordId>? DerivedFromCkRecordId { get; }

    /// <summary>
    ///     Gets or sets the construction kit id
    /// </summary>
    public CkId<CkRecordId> CkRecordId { get; }

    /// <summary>
    ///     If true, the type cannot be inherited again
    /// </summary>
    public bool IsFinal { get; }

    /// <summary>
    ///     If true, the type cannot be instantiated by a runtime entity
    /// </summary>
    public bool IsAbstract { get; }

    /// <summary>
    /// Returns a list of base records of the give construction kit record
    /// </summary>
    public IReadOnlyCollection<CkGraphRecordInheritance> BaseRecords { get; }
    
    /// <summary>
    /// Returns a list of derived records of the given construction kit record
    /// </summary>
    public IReadOnlyCollection<CkGraphRecordInheritance> DerivedRecords { get; }

    /// <summary>
    ///     Gets or sets a list of attributes that are defined by the current record
    /// </summary>
    public IReadOnlyCollection<CkTypeAttributeDto> DefinedAttributes { get; } 
    
    /// <summary>
    ///     Gets or sets a list of attributes including inherited ones.
    /// </summary>
    public IReadOnlyDictionary<CkId<CkAttributeId>, CkTypeAttributeDto> AllAttributes { get; } 

    /// <summary>
    /// Returns a string that describes the inheritance chain
    /// </summary>
    [JsonIgnore]
    public string Path => CkRecordId + ": " + string.Join("->", BaseRecords.Select(x => x.BaseCkRecordId));

    /// <summary>
    /// Adds a list of base records of the current record
    /// </summary>
    /// <param name="baseRecordList"></param>
    internal void AddBaseRecords(IEnumerable<CkGraphRecordInheritance> baseRecordList)
    {
        _baseRecords.AddRange(baseRecordList);
    }
    
    /// <summary>
    /// Adds a list of derived records of the current record
    /// </summary>
    /// <param name="ckGraphRecordInheritance"></param>
    internal void AddDerivedRecords(CkGraphRecordInheritance ckGraphRecordInheritance)
    {
        _derivedRecords.Add(ckGraphRecordInheritance);
    }
    
    /// <summary>
    /// Adds a attribute to the current record
    /// </summary>
    /// <param name="ckTypeAttributeDto"></param>
    internal bool TryAddAttribute(CkTypeAttributeDto ckTypeAttributeDto)
    {
        if (_allAttributes.ContainsKey(ckTypeAttributeDto.CkAttributeId))
        {
            return false;
        }
        _allAttributes.Add(ckTypeAttributeDto.CkAttributeId, ckTypeAttributeDto);
        return true;
    }
    
    /// <summary>
    /// Returns a list of derived records of the given construction kit record
    /// </summary>
    /// <param name="includeSelf">When true, the current record is included to the list</param>
    /// <returns></returns>
    public IReadOnlyCollection<CkId<CkRecordId>> GetAllDerivedRecords(bool includeSelf)
    {
        var list = new List<CkId<CkRecordId>>();
        if (includeSelf)
        {
            list.Add(CkRecordId);
        }
        list.AddRange(_derivedRecords.Select(x=> x.InheritorCkRecordId));

        return list;
    }

    /// <summary>
    /// Returns a list of base records of the given construction kit record
    /// </summary>
    /// <param name="includeSelf">When true, the current record is included to the list</param>
    /// <returns></returns>
    public IReadOnlyCollection<CkId<CkRecordId>> GetBaseTypes(bool includeSelf)
    {
        var list = new List<CkId<CkRecordId>>();
        if (includeSelf)
        {
            list.Add(CkRecordId);
        }
        list.AddRange(_derivedRecords.Select(x=> x.BaseCkRecordId));

        return list;
    }
}