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
public class CkRecordGraph
{
    private readonly List<CkGraphRecordInheritance> _baseRecords;
    
    /// <summary>
    /// Creates a new instance of <see cref="CkRecordGraph"/>.
    /// </summary>
    /// <param name="ckRecordId"></param>
    /// <param name="isAbstract"></param>
    /// <param name="isFinal"></param>
    public CkRecordGraph(CkId<CkRecordId> ckRecordId, bool isAbstract, bool isFinal)
    {
        CkRecordId = ckRecordId;
        IsAbstract = isAbstract;
        IsFinal = isFinal;
        _baseRecords = new List<CkGraphRecordInheritance>();
        BaseRecords = new ReadOnlyCollection<CkGraphRecordInheritance>(_baseRecords);
        Attributes = new List<CkTypeAttributeDto>();
    }

    /// <summary>
    /// Creates a new instance of <see cref="CkRecordGraph"/>.
    /// </summary>
    /// <param name="ckRecordId"></param>
    /// <param name="isAbstract"></param>
    /// <param name="isFinal"></param>
    /// <param name="baseRecords"></param>
    /// <param name="attributes"></param>
    [JsonConstructor]
    public CkRecordGraph(CkId<CkRecordId> ckRecordId, bool isAbstract, bool isFinal, IReadOnlyCollection<CkGraphRecordInheritance> baseRecords, 
        ICollection<CkTypeAttributeDto> attributes)
    {
        CkRecordId = ckRecordId;
        IsAbstract = isAbstract;
        IsFinal = isFinal;
        _baseRecords = new List<CkGraphRecordInheritance>(baseRecords);
        BaseRecords = new ReadOnlyCollection<CkGraphRecordInheritance>(_baseRecords);
        Attributes = attributes;
    }

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
    /// Returns a list of base records of the give construction kit records
    /// </summary>
    public IReadOnlyCollection<CkGraphRecordInheritance> BaseRecords { get; }

    /// <summary>
    ///     Gets or sets a list of attributes including inherited ones.
    /// </summary>
    public ICollection<CkTypeAttributeDto> Attributes { get; } 

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
}