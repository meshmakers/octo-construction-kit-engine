using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine.Repositories.Query;

internal class AssociationMultipleOriginResultSet(Dictionary<RtEntityId, ResultSet<RtAssociation>> queryMultipleResult)
    : Dictionary<RtEntityId, IResultSet<RtAssociation>>(queryMultipleResult.ToDictionary(k => k.Key,
            v => (IResultSet<RtAssociation>) v.Value)), IMultipleOriginResultSet<RtAssociation>;
