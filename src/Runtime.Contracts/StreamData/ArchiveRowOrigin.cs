namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Whether an archive row was entered manually by a person or delivered automatically by a system
/// (adapter, pipeline, import). Recorded on every row, for every archive, independent of the data
/// model — so a domain quality code (for example the metering data quality L1–L3) only has to say how
/// good a value is, not where it came from.
/// </summary>
/// <remarks>
/// The writer declares the origin; a write that declares nothing counts as <see cref="Automated" />.
/// Rows written before the origin was recorded carry none and read as automated.
/// <para>
/// An archive can rank competing writes by origin by naming <see cref="ArchiveConflictKey.OriginKey" />
/// in its <c>ConflictPrecedence</c>: a manual value then is permanent — an automated delivery never
/// replaces it, whatever its other keys say — and only another manual write changes it.
/// </para>
/// </remarks>
public enum ArchiveRowOrigin
{
    /// <summary>Delivered by a system: an adapter, a pipeline, an import. The default.</summary>
    Automated = 0,

    /// <summary>Entered or corrected by a person.</summary>
    Manual = 1
}
