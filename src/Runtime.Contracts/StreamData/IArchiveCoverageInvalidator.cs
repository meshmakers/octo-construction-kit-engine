using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Drops memoised <see cref="ArchiveCoverage"/> entries (AB#5157) so a coverage answer that a
/// platform operation has just invalidated is re-measured on the next request instead of waiting
/// out the cache TTL.
/// </summary>
/// <remarks>
/// <para>
/// The platform calls this on the events that change an archive's stored range in a way the TTL
/// alone would hide for too long. The engine itself owns two of them: an archive delete (that
/// archive) and the completion of a recompute or backfill job (the recomputed rollup). The
/// tenant-wide events — dropping a tenant's stream data, or disabling the stream data feature —
/// happen in the persistence layer and call <c>Invalidate(tenantId)</c> from there. Ordinary
/// ingest is deliberately <em>not</em> invalidated — it only ever extends the range, and the TTL
/// absorbs it.
/// </para>
/// <para>
/// Invalidation is process-local: it clears the cache of the process that observed the event, not
/// of every host in the cluster. Every consumer of this interface treats it as optional (the
/// coverage cache may not be wired at all), so a missing invalidator degrades to TTL-only
/// freshness rather than to an error.
/// </para>
/// </remarks>
public interface IArchiveCoverageInvalidator
{
    /// <summary>
    /// Drops the cached coverage of <paramref name="archiveRtId"/> within
    /// <paramref name="tenantId"/>, or of every archive of that tenant when
    /// <paramref name="archiveRtId"/> is <c>null</c>. Idempotent; a no-op when nothing is cached.
    /// </summary>
    /// <param name="tenantId">Tenant whose cached coverage is dropped.</param>
    /// <param name="archiveRtId">Archive to drop; <c>null</c> drops the whole tenant.</param>
    void Invalidate(string tenantId, OctoObjectId? archiveRtId = null);
}
