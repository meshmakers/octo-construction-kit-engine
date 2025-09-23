using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
///     Represents a construction kit record in the dependency graph
/// </summary>
[DebuggerDisplay("{" + nameof(Path) + "}")]
public class CkRecordGraph : CkTypeWithAttributesGraph
{
    private readonly List<CkGraphRecordInheritance> _baseRecords;
    private readonly List<CkGraphRecordInheritance> _derivedRecords;

    /// <summary>
    ///     Creates a new instance of <see cref="CkRecordGraph" />.
    /// </summary>
    /// <param name="ckRecordId"></param>
    /// <param name="ckRecordDto"></param>
    public CkRecordGraph(CkId<CkRecordId> ckRecordId, CkRecordDto ckRecordDto)
        : base(ckRecordDto)
    {
        CkRecordId = ckRecordId;
        IsAbstract = ckRecordDto.IsAbstract;
        IsFinal = ckRecordDto.IsFinal;
        DerivedFromCkRecordId = ckRecordDto.DerivedFromCkRecordId;
        Description = ckRecordDto.Description;
        _baseRecords = [];
        _derivedRecords = [];
        BaseRecords = new ReadOnlyCollection<CkGraphRecordInheritance>(_baseRecords);
        DerivedRecords = new ReadOnlyCollection<CkGraphRecordInheritance>(_derivedRecords);
    }

    /// <summary>
    ///     Creates a new instance of <see cref="CkRecordGraph" />.
    /// </summary>
    /// <param name="ckRecordId"></param>
    /// <param name="isAbstract"></param>
    /// <param name="isFinal"></param>
    /// <param name="baseRecords"></param>
    /// <param name="derivedFromCkRecordId"></param>
    /// <param name="derivedRecords"></param>
    /// <param name="definedAttributes"></param>
    /// <param name="allAttributes"></param>
    /// <param name="description"></param>
    [JsonConstructor]
    public CkRecordGraph(CkId<CkRecordId> ckRecordId, bool isAbstract, bool isFinal,
        IReadOnlyCollection<CkGraphRecordInheritance> baseRecords,
        CkId<CkRecordId>? derivedFromCkRecordId,
        IReadOnlyCollection<CkGraphRecordInheritance> derivedRecords,
        IReadOnlyCollection<CkTypeAttributeDto> definedAttributes,
        IReadOnlyDictionary<CkId<CkAttributeId>, CkTypeAttributeGraph> allAttributes, string description)
        : base(definedAttributes, allAttributes)
    {
        CkRecordId = ckRecordId;
        IsAbstract = isAbstract;
        IsFinal = isFinal;
        DerivedFromCkRecordId = derivedFromCkRecordId;
        Description = description;

        _baseRecords = new List<CkGraphRecordInheritance>(baseRecords);
        _derivedRecords = new List<CkGraphRecordInheritance>(derivedRecords);
        BaseRecords = new ReadOnlyCollection<CkGraphRecordInheritance>(_baseRecords);
        DerivedRecords = new ReadOnlyCollection<CkGraphRecordInheritance>(_derivedRecords);
    }

    /// <summary>
    ///     Defines the base record of this record.
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
    ///     Returns a list of base records of the give construction kit record
    /// </summary>
    public IReadOnlyCollection<CkGraphRecordInheritance> BaseRecords { get; }

    /// <summary>
    ///     Returns a list of derived records of the given construction kit record
    /// </summary>
    public IReadOnlyCollection<CkGraphRecordInheritance> DerivedRecords { get; }
    
    /// <summary>
    ///     An optional description of the record
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    ///     Returns a string that describes the inheritance chain
    /// </summary>
    [JsonIgnore]
    public string Path => CkRecordId + ": " + string.Join("->", BaseRecords.Select(x => x.BaseCkRecordId));

    /// <summary>
    ///     Adds a list of base records of the current record
    /// </summary>
    /// <param name="baseRecordList"></param>
    internal void AddBaseRecords(IEnumerable<CkGraphRecordInheritance> baseRecordList)
    {
        _baseRecords.AddRange(baseRecordList);
    }

    /// <summary>
    ///     Adds a list of derived records of the current record
    /// </summary>
    /// <param name="ckGraphRecordInheritance"></param>
    internal void AddDerivedRecords(CkGraphRecordInheritance ckGraphRecordInheritance)
    {
        _derivedRecords.Add(ckGraphRecordInheritance);
    }

    /// <summary>
    ///     Returns a list of derived records of the given construction kit record
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

        list.AddRange(_derivedRecords.Select(x => x.InheritorCkRecordId));

        return list;
    }

    /// <summary>
    ///     Returns a list of base records of the given construction kit record
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

        list.AddRange(_derivedRecords.Select(x => x.BaseCkRecordId));

        return list;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return CkRecordId.ToString();
    }
}