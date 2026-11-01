using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.Exchange;

/// <summary>
/// A column an import added to an activated archive (AB#7997). The seed declared a column the archive did not have;
/// instead of writing the definition without its storage, the import handed it to the archive lifecycle: an ingested
/// column through <c>ALTER TABLE … ADD COLUMN</c>, a computed column through the computed-column add (Pending, storage,
/// background backfill job, Active).
/// </summary>
/// <param name="ArchiveRtId">The archive.</param>
/// <param name="ArchiveWellKnownName">The well-known name of the archive, if it has one.</param>
/// <param name="Column">The attribute path of an ingested column, the name of a computed column.</param>
/// <param name="IsComputed">True for a computed column.</param>
/// <param name="BackfillJobRtId">The queued backfill job of a computed column, if a job store is wired.</param>
/// <param name="Error">Why the column could not be added; <c>null</c> when it was added (or already present).</param>
public sealed record RtImportArchiveColumnAddition(
    OctoObjectId ArchiveRtId,
    string? ArchiveWellKnownName,
    string Column,
    bool IsComputed,
    OctoObjectId? BackfillJobRtId,
    string? Error)
{
    /// <summary>True when the column is in place (or its backfill is queued).</summary>
    public bool Succeeded => Error is null;
}
