namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
/// Tuple of a construction kit type, role and direction to identify an association
/// </summary>
/// <param name="CkTypeId">Construction Kit type id</param>
/// <param name="CkAssociationRoleId">Construction Kit association role id</param>
public record CkTypeAssociationTuple(CkId<CkTypeId> CkTypeId, CkId<CkAssociationRoleId> CkAssociationRoleId);