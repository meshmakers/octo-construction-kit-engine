using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
/// Pair of rt association roles id and direction.
/// </summary>
/// <param name="CkRoleId">Association role id</param>
/// <param name="Direction">>Direction of the association</param>
/// <param name="TargetCkTypeId">>Target construction kit type id</param>
// ReSharper disable once ClassNeverInstantiated.Global
public record NavigationPair(CkId<CkAssociationRoleId> CkRoleId, GraphDirections Direction, CkId<CkTypeId> TargetCkTypeId);