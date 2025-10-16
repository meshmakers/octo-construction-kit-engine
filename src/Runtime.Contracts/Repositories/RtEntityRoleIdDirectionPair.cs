using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
/// Pair of rt entity id, association role id and direction.
/// </summary>
/// <param name="RtEntityId">Runtime entity id</param>
/// <param name="CkRoleId">Association role id</param>
/// <param name="Direction">>Direction of the association</param>
public record RtEntityRoleIdDirectionPair(RtEntityId RtEntityId, RtCkId<CkAssociationRoleId> CkRoleId, GraphDirections Direction);