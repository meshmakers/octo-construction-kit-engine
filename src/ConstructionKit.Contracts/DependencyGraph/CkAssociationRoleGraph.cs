using System.Diagnostics;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
/// Represents an association role in the dependency graph
/// </summary>
[DebuggerDisplay("{" + nameof(CkRoleId) + "}")]
public class CkAssociationRoleGraph
{
    public CkAssociationRoleGraph(CkId<CkAssociationRoleId> ckAssociationCkRoleId, CkAssociationRoleDto associationRoleDto)
    {
        CkRoleId = ckAssociationCkRoleId;
        InboundName = associationRoleDto.InboundName;
        OutboundName = associationRoleDto.OutboundName;
        InboundMultiplicity = associationRoleDto.InboundMultiplicity;
        OutboundMultiplicity = associationRoleDto.OutboundMultiplicity;
    }
    
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
}