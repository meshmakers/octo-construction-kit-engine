using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

/// <summary>
/// Stateful in-memory <see cref="IArchiveRuntimeStore"/> for the computed-column tests (AB#8002): the
/// backfill job re-reads the archive after the lifecycle wrote to it, so a fake that always returns
/// the same snapshot no longer models the contract. Wrap it in a FakeItEasy fake to keep call-order
/// assertions.
/// </summary>
internal sealed class InMemoryArchiveRuntimeStore : IArchiveRuntimeStore
{
    private readonly Dictionary<OctoObjectId, ArchiveSnapshot> _archives = new();

    public void Put(ArchiveSnapshot snapshot) => _archives[snapshot.RtId] = snapshot;

    public CkArchiveColumnSpec? Column(OctoObjectId archiveRtId, string name) =>
        _archives.TryGetValue(archiveRtId, out var archive)
            ? archive.Columns.FirstOrDefault(c => c.Name == name)
            : null;

    public Task<ArchiveSnapshot?> GetAsync(OctoObjectId archiveRtId) =>
        Task.FromResult(_archives.TryGetValue(archiveRtId, out var archive) ? archive : null);

    public Task SetStatusAsync(OctoObjectId archiveRtId, CkArchiveStatus newStatus)
    {
        _archives[archiveRtId] = _archives[archiveRtId] with { Status = newStatus };
        return Task.CompletedTask;
    }

    public Task ArchiveEntityAsync(OctoObjectId archiveRtId)
    {
        _archives.Remove(archiveRtId);
        return Task.CompletedTask;
    }

#pragma warning disable CS1998 // async iterator over an in-memory snapshot
    public async IAsyncEnumerable<ArchiveSnapshot> EnumerateAsync()
    {
        foreach (var archive in _archives.Values.ToList())
        {
            yield return archive;
        }
    }
#pragma warning restore CS1998

    public Task AddComputedColumnAsync(OctoObjectId archiveRtId, CkArchiveColumnSpec column)
    {
        var archive = _archives[archiveRtId];
        _archives[archiveRtId] = archive with
        {
            Columns = archive.Columns.Where(c => c.Name != column.Name).Append(column).ToList(),
        };
        return Task.CompletedTask;
    }

    // AB#7997: an ingested column has no name; appended as is (the computed add above replaces by name).
    public Task AddIngestedColumnAsync(OctoObjectId archiveRtId, CkArchiveColumnSpec column)
    {
        var archive = _archives[archiveRtId];
        _archives[archiveRtId] = archive with { Columns = archive.Columns.Append(column).ToList() };
        return Task.CompletedTask;
    }

    public Task SetComputedColumnStateAsync(OctoObjectId archiveRtId, string name, ComputedColumnState state) =>
        Change(archiveRtId, name, c => c with { ComputedState = state });

    public Task RemoveComputedColumnAsync(OctoObjectId archiveRtId, string name)
    {
        var archive = _archives[archiveRtId];
        _archives[archiveRtId] = archive with { Columns = archive.Columns.Where(c => c.Name != name).ToList() };
        return Task.CompletedTask;
    }

    public Task SetPendingFormulaAsync(OctoObjectId archiveRtId, string name, string pendingFormula) =>
        Change(archiveRtId, name, c => c with { PendingFormula = pendingFormula });

    public Task SwapComputedColumnFormulaAsync(OctoObjectId archiveRtId, string name, string newFormula, int newVersion) =>
        Change(archiveRtId, name, c => c with { Formula = newFormula, ComputedVersion = newVersion, PendingFormula = null });

    public Task ClearPendingFormulaAsync(OctoObjectId archiveRtId, string name) =>
        Change(archiveRtId, name, c => c with { PendingFormula = null });

    private Task Change(OctoObjectId archiveRtId, string name, Func<CkArchiveColumnSpec, CkArchiveColumnSpec> change)
    {
        var archive = _archives[archiveRtId];
        _archives[archiveRtId] = archive with
        {
            Columns = archive.Columns.Select(c => c.Name == name ? change(c) : c).ToList(),
        };
        return Task.CompletedTask;
    }
}

/// <summary>In-memory <see cref="IRecomputeJobStore"/> that keeps every state a job went through.</summary>
internal sealed class InMemoryRecomputeJobStore : IRecomputeJobStore
{
    private readonly Dictionary<OctoObjectId, RecomputeJobSnapshot> _jobs = new();
    private readonly object _lock = new();

    /// <summary>Every write, in order — lets a test see Pending → Running → Completed.</summary>
    public List<RecomputeJobSnapshot> History { get; } = new();

    public IReadOnlyList<RecomputeJobSnapshot> All
    {
        get
        {
            lock (_lock)
            {
                return _jobs.Values.OrderBy(j => j.RtId.ToString(), StringComparer.Ordinal).ToList();
            }
        }
    }

    public Task<OctoObjectId> CreateAsync(RecomputeJobSnapshot job)
    {
        var rtId = OctoObjectId.GenerateNewId();
        lock (_lock)
        {
            _jobs[rtId] = job with { RtId = rtId };
            History.Add(_jobs[rtId]);
        }

        return Task.FromResult(rtId);
    }

    public Task UpdateAsync(RecomputeJobSnapshot job)
    {
        lock (_lock)
        {
            _jobs[job.RtId] = job;
            History.Add(job);
        }

        return Task.CompletedTask;
    }

    public Task<RecomputeJobSnapshot?> GetAsync(OctoObjectId jobRtId)
    {
        lock (_lock)
        {
            return Task.FromResult(_jobs.TryGetValue(jobRtId, out var job) ? job : null);
        }
    }

    public Task<IReadOnlyList<RecomputeJobSnapshot>> GetForArchiveAsync(OctoObjectId archiveRtId, int limit)
    {
        lock (_lock)
        {
            IReadOnlyList<RecomputeJobSnapshot> jobs = _jobs.Values
                .Where(j => j.ArchiveRtId == archiveRtId)
                .OrderByDescending(j => j.RtId.ToString(), StringComparer.Ordinal)
                .Take(limit)
                .ToList();
            return Task.FromResult(jobs);
        }
    }

    public Task<RecomputeJobSnapshot?> GetActiveForArchiveAsync(OctoObjectId archiveRtId)
    {
        lock (_lock)
        {
            return Task.FromResult(_jobs.Values
                .Where(j => j.ArchiveRtId == archiveRtId && j.IsActive && !j.IsComputedColumnBackfill)
                .OrderByDescending(j => j.RtId.ToString(), StringComparer.Ordinal)
                .FirstOrDefault());
        }
    }
}
