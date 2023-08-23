using System.Collections.ObjectModel;
using System.Diagnostics;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
/// Represents a construction kit type in the dependency graph
/// </summary>
[DebuggerDisplay("{" + nameof(Path) + "}")]
public class CkTypeGraph
{
    private readonly List<CkGraphTypeInheritance> _baseTypes;
    
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="ckTypeId"></param>
    /// <param name="isAbstract"></param>
    /// <param name="isFinal"></param>
    public CkTypeGraph(CkId<CkTypeId> ckTypeId, bool isAbstract, bool isFinal)
    {
        CkTypeId = ckTypeId;
        IsAbstract = isAbstract;
        IsFinal = isFinal;
        _baseTypes = new List<CkGraphTypeInheritance>();
        BaseTypes = new ReadOnlyCollection<CkGraphTypeInheritance>(_baseTypes);
        Associations = new();
        Attributes = new List<CkTypeAttributeDto>();
    }

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
    /// Returns a list of base types of the give construction kit type
    /// </summary>
    public IReadOnlyCollection<CkGraphTypeInheritance> BaseTypes { get; }

    /// <summary>
    /// Returns a list of associations including inherited ones.
    /// </summary>
    public CkGraphDirectedAssociations Associations { get; }


    public ICollection<CkTypeIndexDto>? Indexes { get; set; }

    /// <summary>
    ///     Gets or sets a list of attributes including inherited ones.
    /// </summary>
    public ICollection<CkTypeAttributeDto> Attributes { get; } 

    /// <summary>
    /// Returns a string that describes the inheritance chain
    /// </summary>
    public string Path => CkTypeId + ": " + string.Join("->", BaseTypes.Select(x => x.BaseCkTypeId));

    /// <summary>
    /// Adds a list of base types of the current type
    /// </summary>
    /// <param name="baseTypeList"></param>
    public void AddBaseTypes(IEnumerable<CkGraphTypeInheritance> baseTypeList)
    {
        _baseTypes.AddRange(baseTypeList);
    }
}