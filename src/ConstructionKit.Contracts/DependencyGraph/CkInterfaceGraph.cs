using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
///     A CK interface in the dependency graph (CK v2, AB#5667).
/// </summary>
[DebuggerDisplay("CkInterfaceId = {CkInterfaceId}")]
public sealed class CkInterfaceGraph
{
    private readonly Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph> _attributes;
    private readonly List<CkId<CkTypeId>> _implementingTypes;

    /// <summary>
    ///     Creates a new instance from the interface definition. <see cref="Attributes" /> is filled by the reference
    ///     resolver (members merged with their attribute definitions), <see cref="ImplementingTypes" /> by the
    ///     inheritance resolver.
    /// </summary>
    /// <param name="ckInterfaceId">The interface id</param>
    /// <param name="dto">The interface definition</param>
    public CkInterfaceGraph(CkId<CkInterfaceId> ckInterfaceId, CkInterfaceDto dto)
    {
        CkInterfaceId = ckInterfaceId;
        Description = dto.Description;
        DefinedAttributes = dto.Attributes;
        _attributes = new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>();
        _implementingTypes = [];
    }

    /// <summary>
    ///     Creates a new instance from the JSON cache.
    /// </summary>
    /// <param name="ckInterfaceId">The interface id</param>
    /// <param name="description">The description</param>
    /// <param name="attributes">The resolved members</param>
    /// <param name="implementingTypes">The implementing types</param>
    [JsonConstructor]
    public CkInterfaceGraph(CkId<CkInterfaceId> ckInterfaceId, string? description,
        IReadOnlyDictionary<CkId<CkAttributeId>, CkTypeAttributeGraph> attributes,
        IReadOnlyCollection<CkId<CkTypeId>> implementingTypes)
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
}
