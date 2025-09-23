using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Meshmakers.Common.Shared;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
/// Represents a construction kit type with attributes in the dependency graph
/// </summary>
public abstract class CkTypeWithAttributesGraph
{
    private readonly Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph> _allAttributes;
    private readonly Dictionary<string, CkTypeAttributeGraph> _allAttributesByName;

    /// <summary>
    /// Creates a new instance of <see cref="CkTypeWithAttributesGraph"/>.
    /// </summary>
    protected CkTypeWithAttributesGraph(CkTypeWithAttributesDto ckTypeWithAttributesDto)
    {
        _allAttributes = new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>();
        _allAttributesByName = new Dictionary<string, CkTypeAttributeGraph>();
        DefinedAttributes = new ReadOnlyCollection<CkTypeAttributeDto>(ckTypeWithAttributesDto.Attributes ?? []);
        AllAttributes = new ReadOnlyDictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>(_allAttributes);
        AllAttributesByName = new ReadOnlyDictionary<string, CkTypeAttributeGraph>(_allAttributesByName);
    }
    
    /// <summary>
    ///     Creates a new instance of <see cref="CkTypeWithAttributesGraph" />.
    /// </summary>
    /// <param name="definedAttributes"></param>
    /// <param name="allAttributes"></param>
    protected CkTypeWithAttributesGraph(
        IReadOnlyCollection<CkTypeAttributeDto> definedAttributes,
        IReadOnlyDictionary<CkId<CkAttributeId>, CkTypeAttributeGraph> allAttributes)
    {
        _allAttributes = new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>(allAttributes
            .ToDictionary(k => k.Key, v => v.Value));
        _allAttributesByName = new Dictionary<string, CkTypeAttributeGraph>(allAttributes
            .ToDictionary(k => k.Value.AttributeName, v => v.Value));
        DefinedAttributes = new List<CkTypeAttributeDto>(definedAttributes);
        AllAttributes = new ReadOnlyDictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>(_allAttributes);
        AllAttributesByName = new ReadOnlyDictionary<string, CkTypeAttributeGraph>(_allAttributesByName);
    }
    
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
    ///     Adds an attribute to the current type
    /// </summary>
    /// <param name="ckTypeAttributeGraph"></param>
    internal bool TryAddAttribute(CkTypeAttributeGraph ckTypeAttributeGraph)
    {
        // ReSharper disable once CanSimplifyDictionaryLookupWithTryAdd
        if (_allAttributes.ContainsKey(ckTypeAttributeGraph.CkAttributeId))
        {
            return false;
        }

        _allAttributes.Add(ckTypeAttributeGraph.CkAttributeId, ckTypeAttributeGraph);
        _allAttributesByName[ckTypeAttributeGraph.AttributeName] = ckTypeAttributeGraph;
        return true;
    }
}