using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
///     A CK interface in the dependency graph (CK v2, AB#5667; extends/associations/methods/deprecated F1.1-S5).
/// </summary>
[DebuggerDisplay("CkInterfaceId = {CkInterfaceId}")]
public sealed class CkInterfaceGraph
{
    private readonly Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph> _attributes;
    private readonly List<CkId<CkTypeId>> _implementingTypes;
    private Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph> _allAttributes;
    private List<CkId<CkInterfaceId>> _allExtendedInterfaces;
    private List<CkInterfaceAssociationGraph> _allAssociations;
    private Dictionary<string, CkInterfaceMethodGraph> _allMethods;

    /// <summary>
    ///     Creates a new instance from the interface definition. <see cref="Attributes" /> is filled by the reference
    ///     resolver (members merged with their attribute definitions), <see cref="ImplementingTypes" /> by the
    ///     inheritance resolver.
    /// </summary>
    /// <param name="ckInterfaceId">The interface id</param>
    /// <param name="dto">The interface definition</param>
    public CkInterfaceGraph(CkId<CkInterfaceId> ckInterfaceId, CkInterfaceDto dto)
    {
        Visibility = CkModifiers.ResolveVisibility(dto.Visibility);
        CkInterfaceId = ckInterfaceId;
        Description = dto.Description;
        DefinedAttributes = dto.Attributes;
        _attributes = new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>();
        _implementingTypes = [];
        DeclaredExtends = dto.Extends ?? [];
        DefinedAssociations = (dto.Associations ?? [])
            .Select(a => new CkInterfaceAssociationGraph(ckInterfaceId, a)).ToList();
        DefinedMethods = dto.Methods ?? [];
        Deprecated = dto.Deprecated ?? false;
        _allAttributes = _attributes;
        _allExtendedInterfaces = [];
        _allAssociations = DefinedAssociations.ToList();
        _allMethods = DefinedMethods.GroupBy(m => m.MethodId)
            .ToDictionary(g => g.Key, g => new CkInterfaceMethodGraph(ckInterfaceId, g.First()));
    }

    /// <summary>
    ///     Creates a new instance from the JSON cache.
    /// </summary>
    /// <param name="ckInterfaceId">The interface id</param>
    /// <param name="description">The description</param>
    /// <param name="attributes">The resolved members</param>
    /// <param name="implementingTypes">The implementing types</param>
    /// <param name="declaredExtends">F1.1-S5: the declared <c>extends</c></param>
    /// <param name="allExtendedInterfaces">F1.1-S5: the transitive <c>extends</c></param>
    /// <param name="definedAssociations">F1.1-S5: the declared association members</param>
    /// <param name="allAssociations">F1.1-S5: own and inherited association members</param>
    /// <param name="definedMethods">F1.1-S5: the declared methods</param>
    /// <param name="allMethods">F1.1-S5: own and inherited methods</param>
    /// <param name="allAttributes">F1.1-S5: own and inherited attribute members (null = own only)</param>
    /// <param name="deprecated">F1.1-S5: deprecated flag</param>
    [JsonConstructor]
    public CkInterfaceGraph(CkId<CkInterfaceId> ckInterfaceId, string? description,
        IReadOnlyDictionary<CkId<CkAttributeId>, CkTypeAttributeGraph> attributes,
        IReadOnlyCollection<CkId<CkTypeId>> implementingTypes,
        IReadOnlyList<CkId<CkInterfaceId>>? declaredExtends = null,
        IReadOnlyCollection<CkId<CkInterfaceId>>? allExtendedInterfaces = null,
        IReadOnlyList<CkInterfaceAssociationGraph>? definedAssociations = null,
        IReadOnlyList<CkInterfaceAssociationGraph>? allAssociations = null,
        IReadOnlyList<CkMethodDto>? definedMethods = null,
        IReadOnlyDictionary<string, CkInterfaceMethodGraph>? allMethods = null,
        IReadOnlyDictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>? allAttributes = null,
        bool deprecated = false)
    {
        CkInterfaceId = ckInterfaceId;
        Description = description;
        // ReSharper disable once ConstantNullCoalescingCondition — STJ passes null for a missing key
        _attributes = attributes?.ToDictionary(k => k.Key, v => v.Value) ??
                      new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>();
        // ReSharper disable once ConstantNullCoalescingCondition
        _implementingTypes = new List<CkId<CkTypeId>>(implementingTypes ?? []);
        DefinedAttributes = _attributes.Values.Select(a => new CkInterfaceAttributeDto
        {
            CkAttributeId = a.CkAttributeId, AttributeName = a.AttributeName, IsOptional = a.IsOptional
        }).ToList();
        DeclaredExtends = declaredExtends ?? [];
        _allExtendedInterfaces = allExtendedInterfaces?.ToList() ?? [];
        DefinedAssociations = definedAssociations ?? [];
        _allAssociations = allAssociations?.ToList() ?? DefinedAssociations.ToList();
        DefinedMethods = definedMethods ?? [];
        _allMethods = allMethods?.ToDictionary(k => k.Key, v => v.Value) ??
                      DefinedMethods.GroupBy(m => m.MethodId)
                          .ToDictionary(g => g.Key, g => new CkInterfaceMethodGraph(ckInterfaceId, g.First()));
        _allAttributes = allAttributes?.ToDictionary(k => k.Key, v => v.Value) ?? _attributes;
        Deprecated = deprecated;
    }

    /// <summary>
    ///     The interface id, e.g. <c>System.Identity-2.90.0/Named-1</c>.
    /// </summary>
    public CkId<CkInterfaceId> CkInterfaceId { get; }

    /// <summary>
    ///     An optional description of the interface.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    ///     The members as declared in the model.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyCollection<CkInterfaceAttributeDto> DefinedAttributes { get; }

    /// <summary>
    ///     The members merged with their attribute definitions (value type etc.). <c>IsOptional</c> is the member
    ///     optionality; <c>Access</c> is always <see cref="CkAttributeAccessDto.ReadWrite" />.
    /// </summary>
    public IReadOnlyDictionary<CkId<CkAttributeId>, CkTypeAttributeGraph> Attributes => _attributes;

    /// <summary>
    ///     CK v2 (F1.1-S5): own members plus the members of every extended interface (filled by the inheritance
    ///     resolver; own members win on a duplicate attribute id). Implementations are checked against this set.
    /// </summary>
    public IReadOnlyDictionary<CkId<CkAttributeId>, CkTypeAttributeGraph> AllAttributes => _allAttributes;

    /// <summary>
    ///     CK v2 (F1.1-S5): the interfaces as declared in <c>extends</c>.
    /// </summary>
    public IReadOnlyList<CkId<CkInterfaceId>> DeclaredExtends { get; }

    /// <summary>
    ///     CK v2 (F1.1-S5): every interface this one extends, directly or transitively (filled by the inheritance
    ///     resolver; unknown entries and cycles are skipped here and reported by the compiler rules).
    /// </summary>
    public IReadOnlyCollection<CkId<CkInterfaceId>> AllExtendedInterfaces => _allExtendedInterfaces;

    /// <summary>
    ///     CK v2 (F1.1-S5): the association members as declared.
    /// </summary>
    public IReadOnlyList<CkInterfaceAssociationGraph> DefinedAssociations { get; }

    /// <summary>
    ///     CK v2 (F1.1-S5): own association members plus those of every extended interface.
    /// </summary>
    public IReadOnlyList<CkInterfaceAssociationGraph> AllAssociations => _allAssociations;

    /// <summary>
    ///     CK v2 (F1.1-S5): the methods as declared (definitions only).
    /// </summary>
    public IReadOnlyList<CkMethodDto> DefinedMethods { get; }

    /// <summary>
    ///     CK v2 (F1.1-S5): own methods plus those of every extended interface, keyed by method id (own wins).
    /// </summary>
    public IReadOnlyDictionary<string, CkInterfaceMethodGraph> AllMethods => _allMethods;

    /// <summary>
    ///     CK v2 (F1.1-S5): the interface is deprecated; dependents get a compile warning.
    /// </summary>
    public bool Deprecated { get; }

    /// <summary>
    ///     Sets the inherited members (inheritance resolver).
    /// </summary>
    internal void SetInheritedMembers(IReadOnlyCollection<CkId<CkInterfaceId>> allExtendedInterfaces,
        IEnumerable<CkInterfaceGraph> extendedGraphs)
    {
        _allExtendedInterfaces = allExtendedInterfaces.ToList();
        var extended = extendedGraphs.ToList();
        _allAttributes = new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>(_attributes);
        foreach (var member in extended.SelectMany(e => e.Attributes.Values))
        {
            if (!_allAttributes.ContainsKey(member.CkAttributeId))
            {
                _allAttributes.Add(member.CkAttributeId, member);
            }
        }

        _allAssociations = DefinedAssociations.Concat(extended.SelectMany(e => e.DefinedAssociations)).ToList();
        _allMethods = DefinedMethods.GroupBy(m => m.MethodId)
            .ToDictionary(g => g.Key, g => new CkInterfaceMethodGraph(CkInterfaceId, g.First()));
        foreach (var method in extended.SelectMany(e => e.DefinedMethods.Select(m => new CkInterfaceMethodGraph(e.CkInterfaceId, m))))
        {
            if (!_allMethods.ContainsKey(method.Definition.MethodId))
            {
                _allMethods.Add(method.Definition.MethodId, method);
            }
        }
    }

    /// <summary>
    ///     The concrete and abstract types that implement this interface directly or via inheritance (filled by the
    ///     inheritance resolver).
    /// </summary>
    public IReadOnlyCollection<CkId<CkTypeId>> ImplementingTypes => _implementingTypes;

    /// <summary>
    ///     Adds a resolved member; returns false when a member with the same attribute id exists already.
    /// </summary>
    internal bool TryAddAttribute(CkTypeAttributeGraph attribute)
    {
        if (_attributes.ContainsKey(attribute.CkAttributeId))
        {
            return false;
        }

        _attributes.Add(attribute.CkAttributeId, attribute);
        return true;
    }

    /// <summary>
    ///     Adds an implementing type (idempotent).
    /// </summary>
    internal void AddImplementingType(CkId<CkTypeId> ckTypeId)
    {
        if (!_implementingTypes.Contains(ckTypeId))
        {
            _implementingTypes.Add(ckTypeId);
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return CkInterfaceId.ToString();
    }

    /// <summary>
    ///     CK v2 (F1.1-S4): the effective visibility (declared value, otherwise <see cref="CkVisibilityDto.Public" />).
    ///     Settable so a cache written before CK v2 (no key) reads <c>Public</c>.
    /// </summary>
    public CkVisibilityDto Visibility { get; set; } = CkVisibilityDto.Public;
}

/// <summary>
///     CK v2 (F1.1-S5): an association member of an interface with its declaring interface.
/// </summary>
/// <param name="DeclaringCkInterfaceId">The interface that declares the member</param>
/// <param name="Definition">The member definition</param>
public sealed record CkInterfaceAssociationGraph(CkId<CkInterfaceId> DeclaringCkInterfaceId,
    CkInterfaceAssociationDto Definition);

/// <summary>
///     CK v2 (F1.1-S5): a method of an interface (definition only) with its declaring interface.
/// </summary>
/// <param name="DeclaringCkInterfaceId">The interface that declares the method</param>
/// <param name="Definition">The method definition</param>
public sealed record CkInterfaceMethodGraph(CkId<CkInterfaceId> DeclaringCkInterfaceId, CkMethodDto Definition)
{
    /// <summary>
    ///     The effective visibility (declared value, otherwise <see cref="CkVisibilityDto.Public" />).
    /// </summary>
    [JsonIgnore]
    public CkVisibilityDto Visibility => CkModifiers.ResolveVisibility(Definition.Visibility);
}
