using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Meshmakers.Octo.Runtime.Engine.Blueprints;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Blueprints;

/// <summary>
/// AB#7991 - the archives a blueprint seed creates are activated after the import (raw first, rollups in source
/// order), new rollups get a backfill, archives that existed before are never touched, and every failure is reported
/// per archive without failing the apply.
/// </summary>
public class BlueprintArchiveActivatorTests
{
    private const string TenantId = "tenant-a";
    private static readonly BlueprintId Blueprint = new("EnergyIQ.Base-1.1.0");
    private static readonly RtCkId<CkTypeId> RawType = new("System.StreamData/RawArchive");
    private static readonly RtCkId<CkTypeId> RollupType = new("System.StreamData/RollupArchive");
    private static readonly RtCkId<CkTypeId> TargetType = new("EnergyIQ/GridConnection");

    private static readonly OctoObjectId Raw = new("6a0e000000000000000a0007");
    private static readonly OctoObjectId Raw2 = new("6a0e000000000000000a0008");
    private static readonly OctoObjectId Rollup5m = new("6a0e000000000000000b0001");
    private static readonly OctoObjectId Rollup1h = new("6a0e000000000000000b0002");

    private readonly Dictionary<OctoObjectId, ArchiveSnapshot> _archives = [];
    private readonly Dictionary<OctoObjectId, RollupArchiveSnapshot> _rollups = [];
    private readonly HashSet<OctoObjectId> _failingActivation = [];
    private readonly HashSet<OctoObjectId> _withData = [];
    private readonly List<OctoObjectId> _activationOrder = [];
    private readonly List<OctoObjectId> _backfilled = [];
    private readonly List<string> _warnings = [];

    private readonly IArchiveLifecycleService _lifecycle = A.Fake<IArchiveLifecycleService>();
    private readonly IArchiveRuntimeStore _archiveStore = A.Fake<IArchiveRuntimeStore>();
    private readonly IRollupArchiveRuntimeStore _rollupStore = A.Fake<IRollupArchiveRuntimeStore>();
    private readonly IRecomputeOrchestrator _recompute = A.Fake<IRecomputeOrchestrator>();
    private readonly IStreamDataTenantServicesProvider _provider = A.Fake<IStreamDataTenantServicesProvider>();
    private bool _streamDataEnabled = true;

    public BlueprintArchiveActivatorTests()
    {
        A.CallTo(() => _provider.GetAsync(TenantId, A<CancellationToken>._))
            .ReturnsLazily(() => Task.FromResult<StreamDataTenantServices?>(new StreamDataTenantServices(
                _streamDataEnabled, _lifecycle, _archiveStore, _rollupStore, _recompute)));
        A.CallTo(() => _archiveStore.GetAsync(A<OctoObjectId>._))
            .ReturnsLazily((OctoObjectId id) => Task.FromResult(_archives.GetValueOrDefault(id)));
        A.CallTo(() => _archiveStore.SetStatusAsync(A<OctoObjectId>._, A<CkArchiveStatus>._))
            .Invokes((OctoObjectId id, CkArchiveStatus status) => _archives[id] = _archives[id] with { Status = status })
            .Returns(Task.CompletedTask);
        A.CallTo(() => _rollupStore.GetAsync(A<OctoObjectId>._))
            .ReturnsLazily((OctoObjectId id) => Task.FromResult(_rollups.GetValueOrDefault(id)));

        // The real lifecycle refuses a rollup whose sources are not activated (RollupValidator); the fake does too, so
        // a wrong order fails the test.
        A.CallTo(() => _lifecycle.ActivateAsync(A<OctoObjectId>._)).ReturnsLazily((OctoObjectId id) =>
        {
            if (_failingActivation.Contains(id))
            {
                throw new InvalidOperationException("CrateDB is unreachable");
            }

            if (_rollups.TryGetValue(id, out var rollup) &&
                rollup.Sources.Any(s => _archives[s.SourceArchiveRtId].Status != CkArchiveStatus.Activated))
            {
                throw new InvalidOperationException("source not activated");
            }

            _activationOrder.Add(id);
            _archives[id] = _archives[id] with { Status = CkArchiveStatus.Activated };
            return Task.CompletedTask;
        });
        A.CallTo(() => _recompute.EnqueueBackfillFromSourceAsync(A<OctoObjectId>._, A<CancellationToken>._))
            .ReturnsLazily((OctoObjectId id, CancellationToken _) =>
            {
                _backfilled.Add(id);
                var sources = _rollups[id].Sources.Select(s => s.SourceArchiveRtId);
                return Task.FromResult(sources.Any(_withData.Contains)
                    ? new RecomputeJobSnapshot(new OctoObjectId($"cc{_backfilled.Count:x22}"), id,
                        RecomputeJobState.Pending, RecomputeTrigger.Manual, DateTime.UnixEpoch, DateTime.UtcNow, null,
                        null, null, null, null, null, null, null)
                    : null);
            });
    }

    // ---- scenario building ------------------------------------------------------------------

    private void GivenRaw(OctoObjectId id, CkArchiveStatus status = CkArchiveStatus.Created) =>
        _archives[id] = new ArchiveSnapshot(id, TargetType, status, $"raw-{id}", []);

    private void GivenRollup(OctoObjectId id, CkRollupFunction function, params OctoObjectId[] sources)
    {
        var aggregations = new[] { new CkRollupAggregationSpec("ImportPower", function, null) };
        _archives[id] = new ArchiveSnapshot(id, TargetType, CkArchiveStatus.Created, $"rollup-{id}", [])
        {
            RollupAggregations = aggregations
        };
        _rollups[id] = new RollupArchiveSnapshot(id, TargetType, CkArchiveStatus.Created, $"rollup-{id}",
            sources.Select(s => new RollupSourceReference(s)).ToList(), TimeSpan.FromHours(1), TimeSpan.Zero, null,
            aggregations, null);
    }

    private static BlueprintArchiveCandidate New(OctoObjectId id, RtCkId<CkTypeId>? type = null, bool optedOut = false) =>
        new(id, null, type ?? RawType, Blueprint, IsNew: true, optedOut);

    private static BlueprintArchiveCandidate Existing(OctoObjectId id) =>
        new(id, null, RawType, Blueprint, IsNew: false, OptedOut: false);

    private Task<IReadOnlyList<BlueprintArchiveActivation>> Run(params BlueprintArchiveCandidate[] candidates) =>
        new BlueprintArchiveActivator(_provider, NullLogger.Instance)
            .ActivateAsync(TenantId, candidates, _warnings.Add, CancellationToken.None);

    private static BlueprintArchiveActivation Of(IReadOnlyList<BlueprintArchiveActivation> results, OctoObjectId id) =>
        results.Single(r => r.ArchiveRtId == id.ToString());

    // ---- ordering ---------------------------------------------------------------------------

    [Fact]
    public async Task RawAndRollup_AreActivatedRawFirst_AndTheRollupIsBackfilled()
    {
        GivenRaw(Raw);
        GivenRollup(Rollup1h, CkRollupFunction.Avg, Raw);
        _withData.Add(Raw);

        // The seed lists the rollup first; the raw archive is still activated before it.
        var results = await Run(New(Rollup1h, RollupType), New(Raw));

        Assert.Equal([Raw, Rollup1h], _activationOrder);
        Assert.Equal(BlueprintArchiveActivationOutcome.Activated, Of(results, Raw).Outcome);
        Assert.False(Of(results, Raw).IsRollup);
        var rollup = Of(results, Rollup1h);
        Assert.Equal(BlueprintArchiveActivationOutcome.Activated, rollup.Outcome);
        Assert.True(rollup.IsRollup);
        Assert.Equal(BlueprintRollupBackfillOutcome.Queued, rollup.Backfill);
        Assert.NotNull(rollup.BackfillJobRtId);
        Assert.Empty(_warnings);
    }

    [Fact]
    public async Task EmptySource_YieldsNothingToBackfill()
    {
        GivenRaw(Raw);
        GivenRollup(Rollup1h, CkRollupFunction.Avg, Raw);

        var results = await Run(New(Raw), New(Rollup1h, RollupType));

        var rollup = Of(results, Rollup1h);
        Assert.Equal(BlueprintRollupBackfillOutcome.NothingToBackfill, rollup.Backfill);
        Assert.Null(rollup.BackfillJobRtId);
        Assert.Contains("nothing to backfill", rollup.BackfillReason);
        Assert.Empty(_warnings);
    }

    [Fact]
    public async Task RollupOfRollup_IsActivatedAndBackfilledInSourceOrder()
    {
        GivenRaw(Raw);
        GivenRollup(Rollup5m, CkRollupFunction.Avg, Raw);
        GivenRollup(Rollup1h, CkRollupFunction.Avg, Rollup5m);
        _withData.Add(Raw);

        // Dependent rollup first in the seed.
        var results = await Run(New(Rollup1h, RollupType), New(Rollup5m, RollupType), New(Raw));

        Assert.Equal([Raw, Rollup5m, Rollup1h], _activationOrder);
        Assert.Equal([Rollup5m, Rollup1h], _backfilled);
        Assert.Equal(BlueprintRollupBackfillOutcome.Queued, Of(results, Rollup5m).Backfill);
        // The 1-h rollup reads the (still empty) 5-min rollup: it fills when the source backfill propagates.
        var dependent = Of(results, Rollup1h);
        Assert.Equal(BlueprintRollupBackfillOutcome.NothingToBackfill, dependent.Backfill);
        Assert.Contains("propagated", dependent.BackfillReason);
        // Result order follows the seed.
        Assert.Equal([Rollup1h.ToString(), Rollup5m.ToString(), Raw.ToString()], results.Select(r => r.ArchiveRtId));
    }

    [Fact]
    public void SortRollupsBySource_ReportsCycles()
    {
        GivenRollup(Rollup5m, CkRollupFunction.Avg, Rollup1h);
        GivenRollup(Rollup1h, CkRollupFunction.Avg, Rollup5m);

        var ordered = BlueprintArchiveActivator.SortRollupsBySource(
            [New(Rollup5m, RollupType), New(Rollup1h, RollupType)], _rollups, out var cyclic);

        Assert.Empty(ordered);
        Assert.Equal(2, cyclic.Count);
    }

    // ---- idempotency and opt-out -----------------------------------------------------------

    [Fact]
    public async Task PreExistingArchives_AreNeverTouched_WhateverTheirStatus()
    {
        GivenRaw(Raw, CkArchiveStatus.Created);
        GivenRaw(Raw2, CkArchiveStatus.Disabled);

        var results = await Run(Existing(Raw), Existing(Raw2));

        Assert.All(results, r => Assert.Equal(BlueprintArchiveActivationOutcome.SkippedPreExisting, r.Outcome));
        A.CallTo(() => _lifecycle.ActivateAsync(A<OctoObjectId>._)).MustNotHaveHappened();
        A.CallTo(() => _recompute.EnqueueBackfillFromSourceAsync(A<OctoObjectId>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => _archiveStore.SetStatusAsync(A<OctoObjectId>._, A<CkArchiveStatus>._)).MustNotHaveHappened();
        // Nothing new to activate: the tenant's stream-data services are not even resolved.
        A.CallTo(() => _provider.GetAsync(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
        Assert.Equal(CkArchiveStatus.Disabled, _archives[Raw2].Status);
    }

    [Fact]
    public async Task UpdateAddingAnArchive_ActivatesOnlyTheNewOne()
    {
        GivenRaw(Raw, CkArchiveStatus.Created); // left Created on purpose by an operator
        GivenRaw(Raw2);

        var results = await Run(Existing(Raw), New(Raw2));

        Assert.Equal([Raw2], _activationOrder);
        Assert.Equal(CkArchiveStatus.Created, _archives[Raw].Status);
        Assert.Equal(BlueprintArchiveActivationOutcome.SkippedPreExisting, Of(results, Raw).Outcome);
        Assert.Equal(BlueprintArchiveActivationOutcome.Activated, Of(results, Raw2).Outcome);
    }

    [Fact]
    public async Task OptedOutArchive_StaysCreated_AndTheResultSaysWhy()
    {
        GivenRaw(Raw);
        GivenRaw(Raw2);

        var results = await Run(New(Raw, optedOut: true), New(Raw2));

        Assert.Equal([Raw2], _activationOrder);
        Assert.Equal(CkArchiveStatus.Created, _archives[Raw].Status);
        var optedOut = Of(results, Raw);
        Assert.Equal(BlueprintArchiveActivationOutcome.SkippedOptOut, optedOut.Outcome);
        Assert.Contains("archiveActivation.exclude", optedOut.Reason);
    }

    [Fact]
    public async Task DuplicateCandidates_AreActivatedOnce()
    {
        GivenRaw(Raw);

        var results = await Run(New(Raw), New(Raw));

        Assert.Single(results);
        A.CallTo(() => _lifecycle.ActivateAsync(Raw)).MustHaveHappenedOnceExactly();
    }

    // ---- AB#7984 guard ---------------------------------------------------------------------

    [Theory]
    [InlineData(CkRollupFunction.TimeWeightedAvg)]
    [InlineData(CkRollupFunction.StateDuration)]
    public async Task TimeWeightedRollupOverRawSource_IsActivated_ButNotBackfilled(CkRollupFunction function)
    {
        GivenRaw(Raw);
        GivenRollup(Rollup1h, function, Raw);
        _withData.Add(Raw);

        var results = await Run(New(Raw), New(Rollup1h, RollupType));

        var rollup = Of(results, Rollup1h);
        Assert.Equal(BlueprintArchiveActivationOutcome.Activated, rollup.Outcome);
        Assert.Equal(BlueprintRollupBackfillOutcome.NotQueued, rollup.Backfill);
        Assert.Contains("AB#7984", rollup.BackfillReason);
        A.CallTo(() => _recompute.EnqueueBackfillFromSourceAsync(A<OctoObjectId>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        Assert.Contains(_warnings, w => w.Contains("AB#7984"));
    }

    [Fact]
    public async Task TimeWeightedRollupOverWindowedSource_IsBackfilled()
    {
        GivenRaw(Raw);
        GivenRollup(Rollup5m, CkRollupFunction.Avg, Raw);
        GivenRollup(Rollup1h, CkRollupFunction.TimeWeightedAvg, Rollup5m);
        _withData.Add(Rollup5m);

        var results = await Run(New(Raw), New(Rollup5m, RollupType), New(Rollup1h, RollupType));

        Assert.Equal(BlueprintRollupBackfillOutcome.Queued, Of(results, Rollup1h).Backfill);
    }

    // ---- stream data off, failures ---------------------------------------------------------

    [Fact]
    public async Task StreamDataDisabled_LeavesArchivesCreated_WithAWarning()
    {
        _streamDataEnabled = false;
        GivenRaw(Raw);
        GivenRollup(Rollup1h, CkRollupFunction.Avg, Raw);

        var results = await Run(New(Raw), New(Rollup1h, RollupType));

        Assert.All(results, r => Assert.Equal(BlueprintArchiveActivationOutcome.SkippedStreamDataDisabled, r.Outcome));
        A.CallTo(() => _lifecycle.ActivateAsync(A<OctoObjectId>._)).MustNotHaveHappened();
        Assert.Contains(_warnings, w => w.Contains("Stream data is not enabled"));
        Assert.All(_archives.Values, a => Assert.Equal(CkArchiveStatus.Created, a.Status));
    }

    [Fact]
    public async Task NoStreamDataBackend_LeavesArchivesCreated()
    {
        GivenRaw(Raw);

        var results = await new BlueprintArchiveActivator(null, NullLogger.Instance)
            .ActivateAsync(TenantId, [New(Raw)], _warnings.Add, CancellationToken.None);

        Assert.Equal(BlueprintArchiveActivationOutcome.SkippedStreamDataDisabled, results.Single().Outcome);
        Assert.Single(_warnings);
    }

    [Fact]
    public async Task OneFailedActivation_IsReported_TheArchiveIsFailed_AndTheOthersAreActivated()
    {
        GivenRaw(Raw);
        GivenRaw(Raw2);
        GivenRollup(Rollup1h, CkRollupFunction.Avg, Raw);
        GivenRollup(Rollup5m, CkRollupFunction.Avg, Raw2);
        _failingActivation.Add(Raw);

        var results = await Run(New(Raw), New(Raw2), New(Rollup1h, RollupType), New(Rollup5m, RollupType));

        var failed = Of(results, Raw);
        Assert.Equal(BlueprintArchiveActivationOutcome.Failed, failed.Outcome);
        Assert.Contains("CrateDB is unreachable", failed.Reason);
        Assert.Equal(CkArchiveStatus.Failed, _archives[Raw].Status); // retryable

        // The rollup on the failed source is not attempted and is Failed too; the independent pair is activated.
        var dependent = Of(results, Rollup1h);
        Assert.Equal(BlueprintArchiveActivationOutcome.Failed, dependent.Outcome);
        Assert.Contains("was not activated", dependent.Reason);
        Assert.Equal(CkArchiveStatus.Failed, _archives[Rollup1h].Status);
        A.CallTo(() => _lifecycle.ActivateAsync(Rollup1h)).MustNotHaveHappened();

        Assert.Equal(BlueprintArchiveActivationOutcome.Activated, Of(results, Raw2).Outcome);
        Assert.Equal(BlueprintArchiveActivationOutcome.Activated, Of(results, Rollup5m).Outcome);
        Assert.Equal(2, _warnings.Count(w => w.Contains("could not be activated")));
    }

    [Fact]
    public async Task FailedBackfill_IsReported_TheRollupStaysActivated()
    {
        GivenRaw(Raw);
        GivenRollup(Rollup1h, CkRollupFunction.Avg, Raw);
        A.CallTo(() => _recompute.EnqueueBackfillFromSourceAsync(Rollup1h, A<CancellationToken>._))
            .Throws(new InvalidOperationException("job store down"));

        var results = await Run(New(Raw), New(Rollup1h, RollupType));

        var rollup = Of(results, Rollup1h);
        Assert.Equal(BlueprintArchiveActivationOutcome.Activated, rollup.Outcome);
        Assert.Equal(BlueprintRollupBackfillOutcome.Failed, rollup.Backfill);
        Assert.Contains("job store down", rollup.BackfillReason);
    }

    // ---- collect (before the import) -------------------------------------------------------

    [Fact]
    public async Task Collect_MarksArchivesTheTenantHolds_AndAppliesTheOptOut()
    {
        var repository = A.Fake<IRuntimeRepository>();
        A.CallTo(() => repository.GetRtEntitiesByIdAsync(A<IOctoSession>._, RawType, A<IReadOnlyList<OctoObjectId>>._,
                A<RtEntityQueryOptions>._, A<int?>._, A<int?>._))
            .ReturnsLazily(() =>
            {
                var set = A.Fake<IResultSet<RtEntity>>();
                A.CallTo(() => set.Items).Returns([new RtEntity(RawType, Raw, new Dictionary<string, object?>())]);
                return Task.FromResult(set);
            });
        var seed = new List<RtEntityTcDto>
        {
            new() { RtId = Raw, CkTypeId = RawType, RtWellKnownName = "GridArchive" },
            new() { RtId = Raw2, CkTypeId = RawType, RtWellKnownName = "PvArchive" },
            new() { RtId = Rollup1h, CkTypeId = RollupType, RtWellKnownName = "GridRollup1h" },
            new() { RtId = OctoObjectId.Empty, CkTypeId = RawType, RtWellKnownName = "NoIdArchive" },
            new() { RtId = new OctoObjectId("6a0e000000000000000c0001"), CkTypeId = new("EnergyIQ/Site") }
        };
        var meta = new BlueprintMetaRootDto
        {
            BlueprintId = Blueprint,
            ArchiveActivation = new BlueprintArchiveActivationDto
            {
                Exclude = ["GridRollup1h", Raw2.ToString().ToUpperInvariant(), "NotInTheSeed"]
            }
        };
        var warnings = new List<string>();

        var candidates = await BlueprintArchiveActivator.CollectAsync(repository, seed,
            t => t.Equals(RawType) || t.Equals(RollupType), meta, warnings);

        Assert.Equal(3, candidates.Count);
        var grid = candidates.Single(c => c.RtId.Equals(Raw));
        Assert.False(grid.IsNew);
        var pv = candidates.Single(c => c.RtId.Equals(Raw2));
        Assert.True(pv.IsNew);
        Assert.True(pv.OptedOut); // by rtId, case-insensitive
        var rollup = candidates.Single(c => c.RtId.Equals(Rollup1h));
        Assert.True(rollup.IsNew);
        Assert.True(rollup.OptedOut); // by well-known name
        Assert.Contains(warnings, w => w.Contains("NoIdArchive") && w.Contains("no rtId"));
        Assert.Contains(warnings, w => w.Contains("'NotInTheSeed'") && w.Contains("matches no archive"));
        Assert.Equal(2, warnings.Count);
    }

    [Fact]
    public async Task Collect_WhenTheLookupFails_TreatsTheArchivesAsPreExisting()
    {
        var repository = A.Fake<IRuntimeRepository>();
        A.CallTo(() => repository.GetRtEntitiesByIdAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                A<IReadOnlyList<OctoObjectId>>._, A<RtEntityQueryOptions>._, A<int?>._, A<int?>._))
            .Throws(new InvalidOperationException("mongo down"));
        var warnings = new List<string>();

        var candidates = await BlueprintArchiveActivator.CollectAsync(repository,
            [new RtEntityTcDto { RtId = Raw, CkTypeId = RawType }], _ => true,
            new BlueprintMetaRootDto { BlueprintId = Blueprint }, warnings);

        Assert.False(candidates.Single().IsNew);
        Assert.Single(warnings);
    }

    [Fact]
    public async Task Collect_WithoutArchives_DoesNotQueryTheTenant()
    {
        var repository = A.Fake<IRuntimeRepository>();
        var warnings = new List<string>();

        var candidates = await BlueprintArchiveActivator.CollectAsync(repository,
            [new RtEntityTcDto { RtId = Raw, CkTypeId = new("EnergyIQ/Site") }], _ => false,
            new BlueprintMetaRootDto { BlueprintId = Blueprint }, warnings);

        Assert.Empty(candidates);
        Assert.Empty(warnings);
        A.CallTo(() => repository.GetSessionAsync()).MustNotHaveHappened();
    }
}
