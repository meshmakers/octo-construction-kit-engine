using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
/// Represents a pair of origin and target runtime entity identifiers.
/// </summary>
/// <param name="Origin">Origin</param>
/// <param name="Target">Target</param>
/// <param name="AssociationRoleId">Association role identifier</param>
public record RtOriginTargetPair(RtEntityId Origin, RtEntityId Target, CkId<CkAssociationRoleId> AssociationRoleId);