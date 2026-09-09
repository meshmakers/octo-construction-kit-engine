using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// Process-wide memo of measured <see cref="ArchiveCoverage"/> answers (AB#5157), keyed by
/// <c>(tenantId, archiveRtId)</c> with a fixed time-to-live. One instance serves every tenant of
/// the host — the per-tenant <c>TenantContext</c> is created fresh for every resolution, so a
/// cache scoped to it would never hit; the tenant id in the key keeps the tenants apart.
/// </summary>
/// <remarks>
/// <para>
/// A "no coverage" answer (<c>null</c>) is cached like any other — it is the normal state of a
/// fresh archive and just as expensive to measure. An exception thrown by the fetch is never
/// cached: it propagates to the caller and the next request measures again. Two concurrent
/// requests for the same key may both fetch; the later result simply overwrites the earlier one,
/// which is harmless for a measurement that only ever moves forward.
/// </para>
/// <para>
/// Freshness is TTL-only except for the events routed through
/// <see cref="IArchiveCoverageInvalidator"/> (archive delete/clear, tenant-level drop, recompute
/// completion); ordinary ingest is absorbed by the TTL. Invalidation is process-local. See
/// <c>concept-multi-source-rollups.md</c> §7.
/// </para>
/// </remarks>
public sealed class ArchiveCoverageCache : IArchiveCoverageInvalidator
{
    /// <summary>
    /// Default time-to-live of a memoised coverage answer. Hosts override it via
    /// <c>StreamData:Coverage:CacheTtlSeconds</c>.
    /// </summary>
    public static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<(string TenantId, OctoObjectId ArchiveRtId), CacheEntry> _entries = new();
    private readonly TimeSpan _cacheTtl;
    private readonly Func<DateTime> _clock;

    /// <summary>
    /// Creates the cache with the given time-to-live. A zero TTL disables memoisation (every
    /// request fetches); a negative TTL is rejected.
    /// </summary>
    /// <param name="cacheTtl">How long a fetched answer is served without re-measuring.</param>
    /// <param name="clock">Injectable UTC clock for tests; defaults to <see cref="DateTime.UtcNow"/>.</param>
    public ArchiveCoverageCache(TimeSpan cacheTtl, Func<DateTime>? clock = null)
    {
        if (cacheTtl < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(cacheTtl), cacheTtl, "Cache TTL must not be negative.");
        }

        _cacheTtl = cacheTtl;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>The time-to-live this cache was created with.</summary>
    public TimeSpan CacheTtl => _cacheTtl;

    /// <summary>
    /// Returns the memoised coverage of <paramref name="archiveRtId"/> within
    /// <paramref name="tenantId"/> when a fresh entry exists, otherwise invokes
    /// <paramref name="fetch"/>, memoises its answer (including <c>null</c>) and returns it. An
    /// exception from <paramref name="fetch"/> propagates and leaves the cache untouched.
    /// </summary>
    /// <param name="tenantId">Tenant the archive belongs to.</param>
    /// <param name="archiveRtId">Runtime id of the archive.</param>
    /// <param name="fetch">Measures the coverage when no fresh entry exists.</param>
    /// <param name="cancellationToken">Cancellation token handed to <paramref name="fetch"/>.</param>
    public async Task<ArchiveCoverage?> GetOrFetchAsync(
        string tenantId,
        OctoObjectId archiveRtId,
        Func<CancellationToken, Task<ArchiveCoverage?>> fetch,
        CancellationToken cancellationToken)
    {
        if (tenantId is null)
        {
            throw new ArgumentNullException(nameof(tenantId));
        }

        if (fetch is null)
        {
            throw new ArgumentNullException(nameof(fetch));
        }

        var key = (tenantId, archiveRtId);
        var now = _clock();
        if (_entries.TryGetValue(key, out var entry) && now - entry.FetchedAt < _cacheTtl)
        {
            return entry.Coverage;
        }

        // Not cached on failure: the exception propagates and the next request measures again.
        var coverage = await fetch(cancellationToken).ConfigureAwait(false);

        // Stamp the entry with the time the measurement was requested, not completed — the value
        // cannot be fresher than the moment it was asked for, so it expires conservatively.
        _entries[key] = new CacheEntry(coverage, now);
        return coverage;
    }

    /// <inheritdoc />
    public void Invalidate(string tenantId, OctoObjectId? archiveRtId = null)
    {
        if (tenantId is null)
        {
            throw new ArgumentNullException(nameof(tenantId));
        }

        if (archiveRtId is { } rtId)
        {
            _entries.TryRemove((tenantId, rtId), out _);
            return;
        }

        foreach (var key in _entries.Keys)
        {
            if (string.Equals(key.TenantId, tenantId, StringComparison.Ordinal))
            {
                _entries.TryRemove(key, out _);
            }
        }
    }

    private sealed record CacheEntry(ArchiveCoverage? Coverage, DateTime FetchedAt);
}
