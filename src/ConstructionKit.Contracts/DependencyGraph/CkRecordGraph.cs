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
        Visibility = CkModifiers.ResolveVisibility(ckRecordDto.Visibility);
        Derivable = CkModifiers.ResolveDerivable(ckRecordDto.Derivable, 1);
        CkRecordId = ckRecordId;
        IsAbstract = ckRecordDto.IsAbstract;
        IsFinal = ckRecordDto.IsFinal;
        DerivedFromCkRecordId = ckRecordDto.DerivedFromCkRecordId;
        Description = ckRecordDto.Description;
        RecordKey = string.IsNullOrWhiteSpace(ckRecordDto.RecordKey) ? null : ckRecordDto.RecordKey;
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
    ///     Effective record key (AB#5528, concept §4.6): the name of the sub-attribute that
    ///     identifies an element of this record inside a record array - declared on this record or
    ///     inherited from the nearest base record that declares one. <c>null</c> when no record in
    ///     the chain declares a key. Used to carry secret sub-values over per element when a record
    ///     array is replaced (write path, AB#5532).
    /// </summary>
    /// <remarks>
    ///     Deliberately a settable property instead of a constructor parameter: a CK cache written
    ///     by an older engine has no <c>recordKey</c> and keeps <c>null</c>; System.Text.Json applies
    ///     the setter after the <see cref="JsonConstructorAttribute" /> constructor.
    /// </remarks>
    public string? RecordKey { get; set; }

    /// <summary>
    ///     Returns a string that describes the inheritance chain
    /// </summary>
    [JsonIgnore]
    public string Path => CkRecordId + ": " + string.Join("->", BaseRecords.Select(x => x.BaseCkRecordId));

    /// <summary>
    ///     Fills an empty record key from a base record. Nearest-wins: a key already set (declared on
    ///     this record or inherited from a nearer base) is never overwritten.
    /// </summary>
    internal void InheritRecordKey(string? baseRecordKey)
    {
        if (string.IsNullOrWhiteSpace(RecordKey) && !string.IsNullOrWhiteSpace(baseRecordKey))
        {
            RecordKey = baseRecordKey;
        }
    }

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

        // AB#5192: this walked _derivedRecords, whose BaseCkRecordId is THIS record — so the method
        // returned its own id once per derived record, and an empty list for a record that has base
        // records but no derived ones, which is the case it exists for. Mirrors CkTypeGraph.GetBaseTypes.
        list.AddRange(_baseRecords.Select(x => x.BaseCkRecordId));

        return list;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return CkRecordId.ToString();
    }

    /// <summary>
    ///     CK v2 (F1.1-S4): the effective visibility (declared value, otherwise <see cref="CkVisibilityDto.Public" />).
    ///     Settable so a cache written before CK v2 (no key) reads <c>Public</c>.
    /// </summary>
    public CkVisibilityDto Visibility { get; set; } = CkVisibilityDto.Public;

    /// <summary>
    ///     CK v2 (F1.1-S4): the effective derivability — the declared value, otherwise <c>Model</c> in a
    ///     <c>ckLanguage: 2</c> model and <c>Any</c> in a v1 model. The CK-language default is applied by the model
    ///     graph (<c>CkModelGraph.ApplyCkV2Modifiers</c>) because the element alone does not know its model's language.
    /// </summary>
    public CkDerivableDto Derivable { get; set; } = CkDerivableDto.Any;
}