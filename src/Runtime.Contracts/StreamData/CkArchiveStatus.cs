namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Lifecycle status of a <c>CkArchive</c> instance. Mirrors the values of the
/// <c>CkArchiveStatus</c> CK enum from the <c>StreamData</c> CK model. Kept here as a plain C#
/// enum so the lifecycle service and other Runtime.Contracts consumers do not have to depend on
/// the generated CK code.
/// </summary>
public enum CkArchiveStatus
{
    /// <summary>
    /// Archive definition exists, but no Crate table has been provisioned. Inserts and queries
    /// are rejected.
    /// </summary>
    Created = 0,

    /// <summary>
    /// Crate table exists, schema is frozen, inserts and queries are accepted.
    /// </summary>
    Activated = 1,

    /// <summary>
    /// Crate table exists, but inserts and queries are rejected. Data is preserved.
    /// </summary>
    Disabled = 2,

    /// <summary>
    /// Activation failed; manual retry required. Inserts and queries are rejected.
    /// </summary>
    Failed = 3,
}
