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
}
