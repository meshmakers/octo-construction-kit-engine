using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
/// Represents an association role in the dependency graph
/// </summary>
[DebuggerDisplay("{" + nameof(CkRoleId) + "}")]
public class CkAssociationRoleGraph
{
    private readonly Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph> _attributes;
    private readonly Dictionary<string, CkTypeAttributeGraph> _allAttributesByName;

    /// <summary>
    /// Creates a new instance of <see cref="CkAssociationRoleGraph"/>.
    /// </summary>
    /// <param name="ckAssociationCkRoleId"></param>
    /// <param name="associationRoleDto"></param>
    public CkAssociationRoleGraph(CkId<CkAssociationRoleId> ckAssociationCkRoleId, CkAssociationRoleDto associationRoleDto)
    {
        CkRoleId = ckAssociationCkRoleId;
        InboundName = associationRoleDto.InboundName;
        OutboundName = associationRoleDto.OutboundName;
        InboundMultiplicity = associationRoleDto.InboundMultiplicity;
        OutboundMultiplicity = associationRoleDto.OutboundMultiplicity;
        DefinedAttributes = new ReadOnlyCollection<CkTypeAttributeDto>(associationRoleDto.Attributes ?? new List<CkTypeAttributeDto>());
        _attributes = new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>();
        _allAttributesByName = new Dictionary<string, CkTypeAttributeGraph>();
        Attributes = new ReadOnlyDictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>(_attributes);
        AllAttributesByName = new ReadOnlyDictionary<string, CkTypeAttributeGraph>(_allAttributesByName);
    }

    /// <summary>
    /// Creates a new instance of <see cref="CkAssociationRoleGraph"/>.
    /// </summary>
    /// <param name="ckRoleId"></param>
    /// <param name="inboundName"></param>
    /// <param name="outboundName"></param>
    /// <param name="inboundMultiplicity"></param>
    /// <param name="outboundMultiplicity"></param>
    /// <param name="definedAttributes"></param>
    /// <param name="attributes"></param>
    [JsonConstructor]
    public CkAssociationRoleGraph(CkId<CkAssociationRoleId> ckRoleId, string inboundName, string outboundName, 
        MultiplicitiesDto inboundMultiplicity, MultiplicitiesDto outboundMultiplicity, 
        IReadOnlyCollection<CkTypeAttributeDto> definedAttributes,
        IReadOnlyDictionary<CkId<CkAttributeId>, CkTypeAttributeGraph> attributes)
    {
        CkRoleId = ckRoleId;
        InboundName = inboundName;
        OutboundName = outboundName;
        InboundMultiplicity = inboundMultiplicity;
        OutboundMultiplicity = outboundMultiplicity;
        DefinedAttributes = new List<CkTypeAttributeDto>(definedAttributes);
        _attributes = new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>(attributes
            .ToDictionary(k=> k.Key, v=> v.Value));
        _allAttributesByName = new Dictionary<string, CkTypeAttributeGraph>(attributes
            .ToDictionary(k => k.Value.AttributeName, v => v.Value));
        Attributes = new ReadOnlyDictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>(_attributes);
        AllAttributesByName = new ReadOnlyDictionary<string, CkTypeAttributeGraph>(_allAttributesByName);
    }
    
    /// <summary>
    /// Returns the ck association id of the association role.
    /// </summary>
    public CkId<CkAssociationRoleId> CkRoleId { get; }

    /// <summary>
    ///     Name of the association for inbound references (e. g. Children)
    /// </summary>
    public string InboundName { get; }

    /// <summary>
    ///     Name of the association for outbound references (e. g. Parent)
    /// </summary>
    public string OutboundName { get; }
    
    /// <summary>
    ///     Multiplicity of the inbound association
    /// </summary>
    public MultiplicitiesDto InboundMultiplicity { get; }

    /// <summary>
    ///     Multiplicity of the outbound association
    /// </summary>
    public MultiplicitiesDto OutboundMultiplicity { get; }
    
    /// <summary>
    ///     Gets or sets a list of attributes for the association role
    /// </summary>
    public IReadOnlyDictionary<CkId<CkAttributeId>, CkTypeAttributeGraph> Attributes { get; set; }
    
    /// <summary>
    ///     Gets or sets a list of attributes that are defined by the current association by name
    /// </summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, CkTypeAttributeGraph> AllAttributesByName { get; }
    
    /// <summary>
    ///     Gets or sets a list of attributes that are defined by the current type
    /// </summary>
    public IReadOnlyCollection<CkTypeAttributeDto> DefinedAttributes { get; }
    
    /// <summary>
    /// Adds a attribute to the current association role
    /// </summary>
    /// <param name="ckTypeAttributeGraph"></param>
    internal bool TryAddAttribute(CkTypeAttributeGraph ckTypeAttributeGraph)
    {
        if (_attributes.ContainsKey(ckTypeAttributeGraph.CkAttributeId))
        {
            return false;
        }

        _attributes.Add(ckTypeAttributeGraph.CkAttributeId, ckTypeAttributeGraph);
        _allAttributesByName[ckTypeAttributeGraph.AttributeName] = ckTypeAttributeGraph;

        return true;
    }
}