using System.Collections.Generic;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.Exchange;

/// <summary>
/// Records noteworthy events of the RT model import path (<see cref="IImportRtModelCommand"/>)
/// so they surface to operators. The engine's default implementation forwards to
/// <c>IAuditEventSink</c>; hosts that want the events in the platform event log replace the
/// sink, not this interface (see the audit-trail architecture notes in the engine repo).
/// </summary>
public interface IRtImportAuditTrail
{
    /// <summary>
    /// Records that an imported entity is missing one or more mandatory attributes — attributes
    /// that are not optional and carry neither default values nor an auto-increment reference,
    /// so nothing can ever fill them. The bulk import path bypasses the entity rule engine, so
    /// such an entity is persisted in a state the CK model forbids (AB#4771/AB#4772) unless
    /// <see cref="RtImportOptions.StrictMandatoryValidation"/> rejects the import.
    /// </summary>
    /// <param name="tenantId">Tenant the import targets.</param>
    /// <param name="ckTypeId">Runtime CK type id of the affected entity.</param>
    /// <param name="rtId">Runtime id of the affected entity.</param>
    /// <param name="missingCkAttributeIds">The missing attributes' CK attribute ids.</param>
    Task RecordMissingMandatoryAttributesAsync(
        string? tenantId,
        RtCkId<CkTypeId> ckTypeId,
        OctoObjectId rtId,
        IReadOnlyList<string> missingCkAttributeIds);

    /// <summary>
    /// Records that one or more association edges were skipped on import because an endpoint
    /// entity exists neither in the imported archive nor in the target tenant (a dangling edge).
    /// The bulk import path writes associations straight into the association collection without
    /// an endpoint check, so such an edge would otherwise be stored as a dead row that no query
    /// can resolve. Skipping keeps the import consistent; the warning tells the operator which
    /// links were dropped (e.g. a permission imported without the role that grants it).
    /// </summary>
    /// <param name="tenantId">Tenant the import targets.</param>
    /// <param name="skippedCount">Total number of association edges skipped.</param>
    /// <param name="sampleEdgeDescriptions">
    /// A bounded sample of the skipped edges (role and both endpoints), for the operator-facing
    /// message; the caller caps the sample size and passes <paramref name="skippedCount"/> as the
    /// true total.
    /// </param>
    Task RecordSkippedDanglingEdgesAsync(
        string? tenantId,
        int skippedCount,
        IReadOnlyList<string> sampleEdgeDescriptions);

    /// <summary>
    /// Records that an Upsert import replaced the edge a to-one association role (multiplicity
    /// One / ZeroOrOne on the origin type) already carried in the tenant, because the import
    /// points that role at a different target. The bulk import upserts edges on
    /// (role, origin, target) and never checked cardinality, so the imported edge used to be
    /// appended next to the existing one — leaving two edges on a role that allows a single one,
    /// which the GraphQL path's cardinality guard rejects and which the UI cannot untangle.
    /// </summary>
    /// <param name="tenantId">Tenant the import targets.</param>
    /// <param name="replacedCount">Number of existing edges deleted in favour of the imported one.</param>
    /// <param name="sampleEdgeDescriptions">
    /// A bounded sample of the replacements (role, origin, old target, new target); the caller caps
    /// the sample size and passes <paramref name="replacedCount"/> as the true total.
    /// </param>
    Task RecordReplacedToOneEdgesAsync(
        string? tenantId,
        int replacedCount,
        IReadOnlyList<string> sampleEdgeDescriptions);
}
