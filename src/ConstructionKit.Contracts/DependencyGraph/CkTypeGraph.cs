using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
///     Represents a construction kit type in the dependency graph
/// </summary>
[DebuggerDisplay("CkTypeId = {CkTypeId}")]
public class CkTypeGraph : CkTypeWithAttributesGraph
{
    private readonly List<CkGraphTypeInheritance> _baseTypes;
    private readonly List<CkGraphTypeInheritance> _derivedTypes;
    private readonly List<CkTypeIndexDto> _indexes;

    /// <summary>
    ///     Creates a new instance of <see cref="CkTypeGraph" />.
    /// </summary>
    /// <param name="ckTypeId"></param>
    /// <param name="ckTypeDto"></param>
    public CkTypeGraph(CkId<CkTypeId> ckTypeId, CkCompiledTypeDto ckTypeDto)
        : base(ckTypeDto)
    {
        CkTypeId = ckTypeId;
        IsAbstract = ckTypeDto.IsAbstract;
        IsFinal = ckTypeDto.IsFinal;
        IsStreamType = ckTypeDto.IsStreamType;
        IsCollectionRoot = ckTypeDto.IsCollectionRoot;
        DerivedFromCkTypeId = ckTypeDto.DerivedFromCkTypeId;
        Description = ckTypeDto.Description;
        _baseTypes = [];
        _derivedTypes = [];
        BaseTypes = new ReadOnlyCollection<CkGraphTypeInheritance>(_baseTypes);
        DerivedTypes = new ReadOnlyCollection<CkGraphTypeInheritance>(_derivedTypes);
        Associations = new CkGraphDirectedAssociations(ckTypeDto.Associations ?? []);
        _indexes = new List<CkTypeIndexDto>(ckTypeDto.Indexes ?? []);
        Indexes = new ReadOnlyCollection<CkTypeIndexDto>(_indexes);
    }

    /// <summary>
    ///     Creates a new instance of <see cref="CkTypeGraph" />.
    /// </summary>
    /// <param name="ckTypeId"></param>
    /// <param name="isAbstract"></param>
    /// <param name="isFinal"></param>
    /// <param name="isCollectionRoot"></param>
    /// <param name="isStreamType"></param>
    /// <param name="baseTypes"></param>
    /// <param name="derivedFromCkTypeId"></param>
    /// <param name="definingCollectionRootCkTypeId"></param>
    /// <param name="derivedTypes"></param>
    /// <param name="definedAttributes"></param>
    /// <param name="allAttributes"></param>
    /// <param name="indexes"></param>
    /// <param name="associations"></param>
    /// <param name="description"></param>
    [JsonConstructor]
    public CkTypeGraph(CkId<CkTypeId> ckTypeId, bool isAbstract, bool isFinal, bool isCollectionRoot, bool isStreamType,
        IReadOnlyCollection<CkGraphTypeInheritance> baseTypes,
        CkId<CkTypeId>? derivedFromCkTypeId,
        CkId<CkTypeId>? definingCollectionRootCkTypeId,
        IReadOnlyCollection<CkGraphTypeInheritance> derivedTypes,
        IReadOnlyCollection<CkTypeAttributeDto> definedAttributes,
        IReadOnlyDictionary<CkId<CkAttributeId>, CkTypeAttributeGraph> allAttributes,
        IReadOnlyCollection<CkTypeIndexDto> indexes, CkGraphDirectedAssociations associations, string description)
        : base(definedAttributes, allAttributes)
    {
        CkTypeId = ckTypeId;
        IsAbstract = isAbstract;
        IsFinal = isFinal;
        IsStreamType = isStreamType;
        IsCollectionRoot = isCollectionRoot;
        DerivedFromCkTypeId = derivedFromCkTypeId;
        DefiningCollectionRootCkTypeId = definingCollectionRootCkTypeId;
        Description = description;

        _baseTypes = new List<CkGraphTypeInheritance>(baseTypes);
        _derivedTypes = new List<CkGraphTypeInheritance>(derivedTypes);
        BaseTypes = new ReadOnlyCollection<CkGraphTypeInheritance>(_baseTypes);
        DerivedTypes = new ReadOnlyCollection<CkGraphTypeInheritance>(_derivedTypes);
        Associations = associations;
        _indexes = new List<CkTypeIndexDto>(indexes);
        Indexes = new ReadOnlyCollection<CkTypeIndexDto>(_indexes);
    }


    /// <summary>
    ///     Defines the base type of this type. Only one type may not have a base type: System/Entity
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
    ///     Gets or sets the defining construction kit type id, which defines the collection in repository.
    /// </summary>
    public CkId<CkTypeId>? DefiningCollectionRootCkTypeId { get; private set; }

    /// <summary>
    ///     Gets or sets a value indicating whether this type is a collection root. When
    ///     true this type creates a collection in the database.
    /// </summary>
    public bool IsCollectionRoot { get; private set; }

    /// <summary>
    ///     Returns a list of base types for the given construction kit type
    /// </summary>
    public IReadOnlyCollection<CkGraphTypeInheritance> BaseTypes { get; }

    /// <summary>
    ///     Returns a list of derived types for the given construction kit type
    /// </summary>
    public IReadOnlyCollection<CkGraphTypeInheritance> DerivedTypes { get; }

    /// <summary>
    ///     Returns a list of associations including inherited ones.
    /// </summary>
    public CkGraphDirectedAssociations Associations { get; }

    /// <summary>
    ///     Returns a list of indexes including inherited ones.
    /// </summary>
    public IReadOnlyCollection<CkTypeIndexDto> Indexes { get; set; }

    /// <summary>
    /// Get or sets a value indicating whether this type is a stream type.
    /// This information is gathered from the types.
    /// </summary>
    public bool IsStreamType { get; set; }
    
    /// <summary>
    ///     An optional description of the type
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    ///     Returns a string that describes the inheritance chain
    /// </summary>
    [JsonIgnore]
    public string Path => CkTypeId + ": " + string.Join("->", BaseTypes.Select(x => x.BaseCkTypeId));

    /// <summary>
    ///     Sets the defining collection rot type id
    /// </summary>
    /// <param name="ckTypeId">CkTypeId of the defining collection</param>
    internal void SetDefiningCollectionCkTypeId(CkId<CkTypeId> ckTypeId)
    {
        DefiningCollectionRootCkTypeId = ckTypeId;
    }

    /// <summary>
    ///     Defines if the current type is a collection
    /// </summary>
    /// <param name="isCollectionRoot">Indicates if the current type is a collection root</param>
    internal void SetIsCollectionRoot(bool isCollectionRoot)
    {
        IsCollectionRoot = isCollectionRoot;
    }

    /// <summary>
    ///     Adds a list of base types of the current type
    /// </summary>
    /// <param name="baseTypeList"></param>
    internal void AddBaseTypes(IEnumerable<CkGraphTypeInheritance> baseTypeList)
    {
        _baseTypes.AddRange(baseTypeList);
    }

    /// <summary>
    ///     Adds a derived types of the current type
    /// </summary>
    /// <param name="ckGraphTypeInheritance"></param>
    internal void AddDerivedTypes(CkGraphTypeInheritance ckGraphTypeInheritance)
    {
        _derivedTypes.Add(ckGraphTypeInheritance);
    }

    /// <summary>
    ///     Appends a list of indexes to the current type
    /// </summary>
    /// <param name="indexesToMerge"></param>
    internal void MergeTextIndexes(IReadOnlyCollection<CkTypeIndexDto> indexesToMerge)
    {
        var textIndex = indexesToMerge.FirstOrDefault(x => x.IndexType == IndexTypeDto.Text);

        foreach (var textIndexToMerge in indexesToMerge.Where(x => x.IndexType == IndexTypeDto.Text))
        {
            if (textIndex == null) // Add text index if it did not exist.
            {
                textIndex = textIndexToMerge;
                _indexes.Add(textIndex);
                continue;
            }

            textIndex.Fields = textIndex.Fields.Concat(textIndexToMerge.Fields).Distinct().ToList();
        }

        // Add ascending indexes, ensure that they are created but not duplicated.
        foreach (var textIndexToMerge in indexesToMerge.Where(x => x.IndexType == IndexTypeDto.Ascending))
        {
            _indexes.Where(x => x.IndexType == IndexTypeDto.Ascending && x.Fields.OrderBy(y => y)
                    .SequenceEqual(textIndexToMerge.Fields.OrderBy(y => y))).ToList()
                .ForEach(x => _indexes.Remove(x));
            _indexes.Add(textIndexToMerge);
        }
    }

    /// <summary>
    ///     Returns a list of derived types for the given construction kit type
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
    ///     Returns a list of base types for the given construction kit type
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
    
    /// <inheritdoc />
    public override string ToString()
    {
        return CkTypeId.ToString();
    }
}