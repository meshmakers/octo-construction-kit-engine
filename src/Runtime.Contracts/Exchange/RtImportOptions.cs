namespace Meshmakers.Octo.Runtime.Contracts.Exchange;

/// <summary>
/// Host-configurable options for the RT model import path (<see cref="IImportRtModelCommand"/>).
/// Hosts typically bind this from configuration (e.g. the <c>Import</c> section, so
/// <c>Import:StrictMandatoryValidation</c>); the engine registers a default instance with every
/// switch off, preserving pre-AB#4772 behaviour.
/// </summary>
public sealed class RtImportOptions
{
    /// <summary>
    /// When <c>true</c>, an import that contains an entity missing a mandatory attribute (an
    /// attribute that is not optional and has neither default values nor an auto-increment
    /// reference — the same predicate the entity rule engine enforces on API inserts) FAILS
    /// before anything is written, listing every violation. When <c>false</c> (default), the
    /// violations are logged as warnings and published as audit events, but the import
    /// proceeds — stage-1 of the AB#4772 two-stage rollout, so existing seed YAMLs surface in
    /// the tenant event log before a hard fail is switched on.
    /// </summary>
    /// <remarks>
    /// Background (AB#4771): the bulk import path bypasses the entity rule engine, so ImportRt
    /// could create entities the CK model forbids — e.g. seeded RollupArchive entities without
    /// the mandatory <c>Archive.Columns</c> attribute, which broke the non-null <c>columns</c>
    /// GraphQL field for the whole archives list on prod-1/energyiq.
    /// </remarks>
    public bool StrictMandatoryValidation { get; set; }
}
