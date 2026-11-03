using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.Exchange;

/// <summary>
/// A formula change of a computed column that an import handed to the archive lifecycle (AB#8255). The seed declared a
/// different formula for a computed column of an activated archive; instead of overwriting the definition (which would
/// leave the stored values and the column version behind), the import kept the live formula and queued the same versioned
/// change with backfill as <c>UpdateComputedColumnFormula</c>: readers keep the previous values until the backfill of the
/// new formula is done and the column version is swapped.
/// </summary>
/// <param name="ArchiveRtId">The archive.</param>
/// <param name="ArchiveWellKnownName">The well-known name of the archive, if it has one.</param>
/// <param name="Column">The name of the computed column.</param>
/// <param name="Formula">The formula the seed declares.</param>
/// <param name="BackfillJobRtId">The queued backfill job, if a job store is wired.</param>
/// <param name="Error">Why the change could not be queued; <c>null</c> when it was queued.</param>
public sealed record RtImportComputedFormulaChange(
    OctoObjectId ArchiveRtId,
    string? ArchiveWellKnownName,
    string Column,
    string Formula,
    OctoObjectId? BackfillJobRtId,
    string? Error)
{
    /// <summary>True when the change is queued.</summary>
    public bool Succeeded => Error is null;
}
