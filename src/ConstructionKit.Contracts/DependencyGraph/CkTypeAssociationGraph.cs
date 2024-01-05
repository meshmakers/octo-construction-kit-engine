using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
///     Represents an association assigned to a type in the graph model.
/// </summary>
public class CkTypeAssociationGraph
{
    /// <summary>
    ///     Creates a new instance of <see cref="CkTypeAssociationGraph" />.
    /// </summary>
    /// <param name="navigationPropertyName">Corresponding inbound/outbound name</param>
    /// <param name="multiplicity">The multiplicity of the target</param>
    /// <param name="originCkTypeId">Origin type id</param>
    /// <param name="ckTypeAssociationDto"></param>
    public CkTypeAssociationGraph(string navigationPropertyName, MultiplicitiesDto multiplicity, CkId<CkTypeId> originCkTypeId,
        CkTypeAssociationDto ckTypeAssociationDto)
    {
        NavigationPropertyName = navigationPropertyName;
        Multiplicity = multiplicity;
        CkRoleId = ckTypeAssociationDto.CkRoleId;
        OriginCkTypeId = originCkTypeId;
        TargetCkTypeId = ckTypeAssociationDto.TargetCkTypeId;
        TargetAttributes = ckTypeAssociationDto.TargetAttributes;
    }

    /// <summary>
    ///     Creates a new instance of <see cref="CkTypeAssociationGraph" />.
    /// </summary>
    /// <param name="navigationPropertyName">Corresponding inbound/outbound name</param>
    /// <param name="multiplicity">The multiplicity of the target</param>
    /// <param name="ckRoleId"></param>
    /// <param name="originCkTypeId"></param>
    /// <param name="targetCkTypeId"></param>
    /// <param name="targetAttributes"></param>
    [JsonConstructor]
    public CkTypeAssociationGraph(string navigationPropertyName, MultiplicitiesDto multiplicity, CkId<CkAssociationRoleId> ckRoleId,
        CkId<CkTypeId> originCkTypeId,
        CkId<CkTypeId> targetCkTypeId, IReadOnlyCollection<CkId<CkAttributeId>>? targetAttributes)
    {
        NavigationPropertyName = navigationPropertyName;
        Multiplicity = multiplicity;
        CkRoleId = ckRoleId;
        OriginCkTypeId = originCkTypeId;
        TargetCkTypeId = targetCkTypeId;
        TargetAttributes = targetAttributes;
    }

    /// <summary>
    ///     Returns the name of the association.
    /// </summary>
    public string NavigationPropertyName { get; }

    /// <summary>
    ///     Returns the target multiplicity.
    /// </summary>
    public MultiplicitiesDto Multiplicity { get; }

    /// <summary>
    ///     Gets or sets the association role id.
    /// </summary>
    public CkId<CkAssociationRoleId> CkRoleId { get; }

    /// <summary>
    ///     Gets or sets the origin CK type id.
    /// </summary>
    public CkId<CkTypeId> OriginCkTypeId { get; }

    /// <summary>
    ///     Gets or sets the target CK type id.
    /// </summary>
    public CkId<CkTypeId> TargetCkTypeId { get; }

    /// <summary>
    ///     Gets or sets a list of attributes of the target ck type id, that are referential integrity attributes
    /// </summary>
    public IReadOnlyCollection<CkId<CkAttributeId>>? TargetAttributes { get; }
}