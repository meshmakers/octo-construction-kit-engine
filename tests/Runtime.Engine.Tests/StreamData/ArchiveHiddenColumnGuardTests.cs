using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

/// <summary>
///     CK v2 (F1.2-S2, AB#5911, review M12): stream-data queries do not enforce attribute access, so an archive
///     whose ingested columns reach a Hidden attribute is refused on activation.
/// </summary>
public class ArchiveHiddenColumnGuardTests
{
    private const string TenantId = "tenant-hidden";
    private const string ModelId = "Test-1.0.0";
    private static readonly OctoObjectId Rt = OctoObjectId.GenerateNewId();

    private readonly IArchiveRuntimeStore _store = A.Fake<IArchiveRuntimeStore>();
    private readonly IStreamDataRepository _repo = A.Fake<IStreamDataRepository>();
    private readonly ICkCacheService _cache = A.Fake<ICkCacheService>();
    private readonly RtCkId<CkTypeId> _sensor;

    public ArchiveHiddenColumnGuardTests()
    {
        var reading = new CkRecordGraph(new CkId<CkRecordId>($"{ModelId}/Reading"), false, false, [], null, [], [],
            new[] { Attr("Value"), Attr("Calibration", CkAttributeAccessDto.Hidden) }.ToDictionary(a => a.CkAttributeId),
            "Reading");
        var wrapper = new CkRecordGraph(new CkId<CkRecordId>($"{ModelId}/Wrapper"), false, false, [], null, [], [],
            new[] { Attr("Label"), Attr("Inner", recordId: reading.CkRecordId) }.ToDictionary(a => a.CkAttributeId),
            "Wrapper");
        A.CallTo(() => _cache.GetRtCkRecord(TenantId, wrapper.CkRecordId.ToRtCkId())).Returns(wrapper);
        var sensor = new CkTypeGraph(new CkId<CkTypeId>($"{ModelId}/Sensor"), false, false, true, [], null, null, [], [],
            new[]
            {
                Attr("Temperature"), Attr("Secret", CkAttributeAccessDto.Hidden), Attr("Status", CkAttributeAccessDto.ReadOnly),
                Attr("Reading", recordId: reading.CkRecordId), Attr("Wrapper", recordId: wrapper.CkRecordId)
            }.ToDictionary(a => a.CkAttributeId), [], new CkGraphDirectedAssociations([]), "Sensor", false);
        _sensor = sensor.CkTypeId.ToRtCkId();
        A.CallTo(() => _cache.GetRtCkType(TenantId, _sensor)).Returns(sensor);
        A.CallTo(() => _cache.GetRtCkRecord(TenantId, reading.CkRecordId.ToRtCkId())).Returns(reading);
        // Only the archives a test declares are rollups.
        A.CallTo(() => _rollups.GetAsync(A<OctoObjectId>._)).Returns((RollupArchiveSnapshot?)null);
        A.CallTo(() => _rollups.CountActiveRollupsForSourceAsync(A<OctoObjectId>._)).Returns(0);
    }

    private static CkTypeAttributeGraph Attr(string name, CkAttributeAccessDto access = CkAttributeAccessDto.ReadWrite,
        CkId<CkRecordId>? recordId = null)
    {
        var attributeId = new CkId<CkAttributeId>($"{ModelId}/{name}");
        var definition = new CkAttributeDto
        {
            AttributeId = name,
            ValueType = recordId == null ? AttributeValueTypesDto.String : AttributeValueTypesDto.Record,
            ValueCkRecordId = recordId
        };
        return new CkTypeAttributeGraph(attributeId,
            new CkTypeAttributeDto { CkAttributeId = attributeId, AttributeName = name, IsOptional = true },
            new CkAttributeGraph(attributeId, definition)) { Access = access };
    }

    private readonly IArchiveAuditTrail _audit = A.Fake<IArchiveAuditTrail>();
    private readonly IRollupArchiveRuntimeStore _rollups = A.Fake<IRollupArchiveRuntimeStore>();

    private ArchiveLifecycleService Sut(bool withCache = true) =>
        new(TenantId, _store, _repo, _audit, NullLogger<ArchiveLifecycleService>.Instance,
            rollupStore: _rollups, ckCacheService: withCache ? _cache : null);

    private void Stub(CkArchiveStatus status, params string[] paths) =>
        A.CallTo(() => _store.GetAsync(Rt)).Returns(new ArchiveSnapshot(Rt, _sensor, status, null,
            paths.Select(p => new CkArchiveColumnSpec(p, true, false)).ToList()));

    [Theory]
    [InlineData("Secret")]
    [InlineData("secret")] // case-insensitive, like the attribute resolution of the data store
    [InlineData("Reading.Calibration")]
    [InlineData("reading[*].calibration")]
    [InlineData("Reading")] // review G3 E-M2: a whole-record column stores the Hidden sub-attribute
    [InlineData("Wrapper")] // ...also transitively through a nested record
    public async Task ColumnReachingAHiddenAttribute_IsRefused(string path)
    {
        Stub(CkArchiveStatus.Created, "Temperature", path);

        var ex = await Assert.ThrowsAsync<HiddenAttributeInArchiveException>(() => Sut().ActivateAsync(Rt));

        Assert.Equal(path, ex.ColumnPath);
        A.CallTo(() => _repo.EnsureArchiveCreatedAsync(A<ArchiveSnapshot>._)).MustNotHaveHappened();
        A.CallTo(() => _store.SetStatusAsync(A<OctoObjectId>._, A<CkArchiveStatus>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task AlreadyActivatedArchive_GainingAHiddenColumn_IsRefused()
    {
        Stub(CkArchiveStatus.Activated, "Secret");

        await Assert.ThrowsAsync<HiddenAttributeInArchiveException>(() => Sut().ActivateAsync(Rt));
        A.CallTo(() => _repo.EnsureArchiveCreatedAsync(A<ArchiveSnapshot>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task RetryActivation_IsGuardedToo()
    {
        Stub(CkArchiveStatus.Failed, "Secret");

        await Assert.ThrowsAsync<HiddenAttributeInArchiveException>(() => Sut().RetryActivationAsync(Rt));
    }

    [Fact]
    public async Task VisibleColumns_Activate()
    {
        Stub(CkArchiveStatus.Created, "Temperature", "Status", "Reading.Value", "Unknown.Path");

        await Sut().ActivateAsync(Rt);

        A.CallTo(() => _store.SetStatusAsync(Rt, CkArchiveStatus.Activated)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task WithoutACkCache_TheCheckIsSkipped()
    {
        Stub(CkArchiveStatus.Created, "Secret");

        await Sut(withCache: false).ActivateAsync(Rt);

        A.CallTo(() => _store.SetStatusAsync(Rt, CkArchiveStatus.Activated)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Revalidation_FailsAnActiveArchiveThatNowReachesHidden()
    {
        Stub(CkArchiveStatus.Activated, "Temperature", "Reading");

        Assert.False(await Sut().RevalidateAccessAsync(Rt));

        // AB#6247: the transition records the reason and the offending columns in one write.
        A.CallTo(() => _store.SetStatusAsync(Rt, CkArchiveStatus.Failed,
                A<string>.That.Contains("'Reading' (reaches the Hidden attribute 'Reading.Calibration')"),
                A<IReadOnlyList<string>>.That.IsSameSequenceAs(new[] { "Reading" })))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Revalidation_KeepsAVisibleArchive()
    {
        Stub(CkArchiveStatus.Activated, "Temperature", "Reading.Value");

        Assert.True(await Sut().RevalidateAccessAsync(Rt));

        A.CallTo(() => _store.SetStatusAsync(A<OctoObjectId>._, A<CkArchiveStatus>._)).MustNotHaveHappened();
        A.CallTo(() => _store.SetStatusAsync(A<OctoObjectId>._, A<CkArchiveStatus>._, A<string?>._,
            A<IReadOnlyList<string>>._)).MustNotHaveHappened();
        // no resume either: the table is not provisioned again
        A.CallTo(_repo).Where(call => call.Method.Name == nameof(IStreamDataRepository.EnsureArchiveCreatedAsync))
            .MustNotHaveHappened();
    }

    private static ArchiveSnapshot Snapshot(CkArchiveStatus status, RtCkId<CkTypeId> type, string[] hidden,
        params string[] paths) =>
        new(Rt, type, status, null, paths.Select(p => new CkArchiveColumnSpec(p, true, false)).ToList())
        {
            HiddenColumns = hidden
        };

    [Fact]
    public async Task Revalidation_RecordsEveryHiddenColumn()
    {
        Stub(CkArchiveStatus.Activated, "Secret", "Temperature", "Wrapper");

        Assert.False(await Sut().RevalidateAccessAsync(Rt));

        A.CallTo(() => _store.SetStatusAsync(Rt, CkArchiveStatus.Failed, A<string>._,
                A<IReadOnlyList<string>>.That.IsSameSequenceAs(new[] { "Secret", "Wrapper" })))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _audit.RecordTransitionAsync(TenantId, Rt, CkArchiveStatus.Activated, CkArchiveStatus.Failed,
            A<string>.That.Contains("'Secret'"))).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Revalidation_OfAFailedOnHiddenArchive_UpdatesTheRecordedColumnsWithoutATransition()
    {
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Snapshot(CkArchiveStatus.Failed, _sensor, ["Secret"], "Secret",
            "Reading"));

        Assert.False(await Sut().RevalidateAccessAsync(Rt));

        A.CallTo(() => _store.SetStatusAsync(Rt, CkArchiveStatus.Failed, A<string>._,
                A<IReadOnlyList<string>>.That.IsSameSequenceAs(new[] { "Secret", "Reading" })))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _audit.RecordTransitionAsync(A<string>._, A<OctoObjectId>._, A<CkArchiveStatus>._,
            A<CkArchiveStatus>._, A<string?>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Redact_DropsTheHiddenColumns_RecordsIt_AndResumesAFailedOnHiddenArchive()
    {
        A.CallTo(() => _store.GetAsync(Rt)).ReturnsNextFromSequence(
            Snapshot(CkArchiveStatus.Failed, _sensor, ["Secret", "Reading"], "Temperature", "Secret", "Reading"),
            Snapshot(CkArchiveStatus.Failed, _sensor, ["Secret", "Reading"], "Temperature"));
        A.CallTo(() => _store.EnumerateAsync()).Returns(AsyncEnumerable.Empty<ArchiveSnapshot>());

        var result = await Sut().RedactHiddenColumnsAsync(Rt);

        Assert.Equal(CkArchiveStatus.Activated, result.Status);
        Assert.Equal(["Secret", "Reading"], result.RemovedColumns);
        A.CallTo(() => _repo.DropIngestedColumnStorageAsync(A<ArchiveSnapshot>._,
                A<IReadOnlyCollection<string>>.That.IsSameSequenceAs(new[] { "Secret", "Reading" }), A<System.Threading.CancellationToken>._))
            .MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => _store.RemoveIngestedColumnsAsync(Rt,
                A<IReadOnlyCollection<string>>.That.IsSameSequenceAs(new[] { "Secret", "Reading" })))
                .MustHaveHappenedOnceExactly())
            .Then(A.CallTo(() => _store.SetStatusAsync(Rt, CkArchiveStatus.Activated)).MustHaveHappenedOnceExactly());
        A.CallTo(() => _audit.RecordTransitionAsync(TenantId, Rt, CkArchiveStatus.Failed, CkArchiveStatus.Failed,
            A<string>.That.Contains("RedactHiddenColumns drops"))).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Redact_OfADisabledArchive_KeepsItsStatus()
    {
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Snapshot(CkArchiveStatus.Disabled, _sensor, [], "Temperature",
            "Secret"));
        A.CallTo(() => _store.EnumerateAsync()).Returns(AsyncEnumerable.Empty<ArchiveSnapshot>());

        var result = await Sut().RedactHiddenColumnsAsync(Rt);

        Assert.Equal(CkArchiveStatus.Disabled, result.Status);
        Assert.Equal(["Secret"], result.RemovedColumns);
        A.CallTo(() => _store.SetStatusAsync(A<OctoObjectId>._, A<CkArchiveStatus>._)).MustNotHaveHappened();
        A.CallTo(() => _store.SetStatusAsync(A<OctoObjectId>._, A<CkArchiveStatus>._, A<string?>._,
            A<IReadOnlyList<string>>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Redact_OfAnActivatedArchiveWithAHiddenColumn_IsRefused()
    {
        Stub(CkArchiveStatus.Activated, "Temperature", "Secret");

        await Assert.ThrowsAsync<InvalidArchiveStateTransitionException>(() => Sut().RedactHiddenColumnsAsync(Rt));

        A.CallTo(() => _repo.DropIngestedColumnStorageAsync(A<ArchiveSnapshot>._, A<IReadOnlyCollection<string>>._,
            A<System.Threading.CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Redact_IsRefusedWhenAComputedColumnReadsAHiddenColumn()
    {
        Stub(CkArchiveStatus.Disabled, "Temperature", "Secret");
        A.CallTo(() => _repo.ValidateComputedColumnsAsync(Rt, A<IReadOnlyList<CkArchiveColumnSpec>>._,
                A<System.Threading.CancellationToken>._))
            .Throws(new ArchivePathInvalidException(Rt, "secret", "a computed column reads it"));

        await Assert.ThrowsAsync<ArchivePathInvalidException>(() => Sut().RedactHiddenColumnsAsync(Rt));

        A.CallTo(() => _repo.ValidateComputedColumnsAsync(Rt,
                A<IReadOnlyList<CkArchiveColumnSpec>>.That.Matches(c => c.Select(x => x.Path).SequenceEqual(new[] { "Temperature" })),
                A<System.Threading.CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _repo.DropIngestedColumnStorageAsync(A<ArchiveSnapshot>._, A<IReadOnlyCollection<string>>._,
            A<System.Threading.CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Redact_IsRefusedWhileAConflictKeyNamesAHiddenColumn()
    {
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Snapshot(CkArchiveStatus.Disabled, _sensor, [], "Temperature", "Secret")
            with
            {
                ConflictPrecedence =
                [
                    new ArchiveConflictKey(ArchiveConflictKey.OriginKey, ConflictKeyOrder.HigherWins),
                    new ArchiveConflictKey("secret", ConflictKeyOrder.HigherWins)
                ]
            });

        var ex = await Assert.ThrowsAsync<InvalidArchiveStateTransitionException>(() => Sut().RedactHiddenColumnsAsync(Rt));

        Assert.Contains("secret", ex.Message);
        A.CallTo(() => _repo.DropIngestedColumnStorageAsync(A<ArchiveSnapshot>._, A<IReadOnlyCollection<string>>._,
            A<System.Threading.CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => _audit.RecordTransitionAsync(A<string>._, A<OctoObjectId>._, A<CkArchiveStatus>._,
            A<CkArchiveStatus>._, A<string?>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Redact_IgnoresTheOriginKey_AndAPendingFormulaOverRemainingColumns()
    {
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Snapshot(CkArchiveStatus.Disabled, _sensor, [], "Temperature", "Secret")
            with
            {
                ConflictPrecedence = [new ArchiveConflictKey(ArchiveConflictKey.OriginKey, ConflictKeyOrder.HigherWins)],
                Columns =
                [
                    new CkArchiveColumnSpec("Temperature", true, false), new CkArchiveColumnSpec("Secret", true, false),
                    new CkArchiveColumnSpec(string.Empty, false, false)
                    {
                        Name = "doubled", Formula = "temperature * 2", PendingFormula = "temperature * 3"
                    }
                ]
            });
        A.CallTo(() => _store.EnumerateAsync()).Returns(AsyncEnumerable.Empty<ArchiveSnapshot>());

        var result = await Sut().RedactHiddenColumnsAsync(Rt);

        Assert.Equal(["Secret"], result.RemovedColumns);
        A.CallTo(() => _repo.ValidateComputedColumnsAsync(Rt, A<IReadOnlyList<CkArchiveColumnSpec>>._,
            A<System.Threading.CancellationToken>._)).MustHaveHappenedTwiceExactly();
    }

    [Fact]
    public async Task Redact_ValidatesAPendingFormulaToo()
    {
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Snapshot(CkArchiveStatus.Disabled, _sensor, [], "Temperature", "Secret")
            with
            {
                Columns =
                [
                    new CkArchiveColumnSpec("Temperature", true, false), new CkArchiveColumnSpec("Secret", true, false),
                    new CkArchiveColumnSpec(string.Empty, false, false)
                    {
                        Name = "doubled", Formula = "temperature * 2", PendingFormula = "secret * 2"
                    }
                ]
            });
        A.CallTo(() => _repo.ValidateComputedColumnsAsync(Rt,
                A<IReadOnlyList<CkArchiveColumnSpec>>.That.Matches(c =>
                    c.Any(x => x.Formula == "secret * 2") && !c.Any(x => x.Path == "Secret")),
                A<System.Threading.CancellationToken>._))
            .Throws(new ArchivePathInvalidException(Rt, "secret", "the pending formula reads it"));

        await Assert.ThrowsAsync<ArchivePathInvalidException>(() => Sut().RedactHiddenColumnsAsync(Rt));

        A.CallTo(() => _repo.DropIngestedColumnStorageAsync(A<ArchiveSnapshot>._, A<IReadOnlyCollection<string>>._,
            A<System.Threading.CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Redact_IsAuditedBeforeTheDrop()
    {
        Stub(CkArchiveStatus.Disabled, "Temperature", "Secret");
        A.CallTo(() => _store.EnumerateAsync()).Returns(AsyncEnumerable.Empty<ArchiveSnapshot>());
        A.CallTo(() => _repo.DropIngestedColumnStorageAsync(A<ArchiveSnapshot>._, A<IReadOnlyCollection<string>>._,
            A<System.Threading.CancellationToken>._)).Throws(new InvalidOperationException("crate down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().RedactHiddenColumnsAsync(Rt));

        A.CallTo(() => _audit.RecordTransitionAsync(TenantId, Rt, CkArchiveStatus.Disabled, CkArchiveStatus.Disabled,
            A<string>.That.Contains("'Secret'"))).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Redact_OfARollup_IsRefused()
    {
        A.CallTo(() => _store.GetAsync(Rt)).Returns(new ArchiveSnapshot(Rt, _sensor, CkArchiveStatus.Failed, null, [])
        {
            RollupAggregations = [new CkRollupAggregationSpec("Secret", CkRollupFunction.Avg, null, null)]
        });

        await Assert.ThrowsAsync<InvalidArchiveStateTransitionException>(() => Sut().RedactHiddenColumnsAsync(Rt));
    }

    [Fact]
    public async Task Revalidation_FailsARollupOfAHiddenAttribute_WithoutMakingItReadable()
    {
        A.CallTo(() => _store.GetAsync(Rt)).Returns(new ArchiveSnapshot(Rt, _sensor, CkArchiveStatus.Activated, null, [])
        {
            RollupAggregations =
            [
                new CkRollupAggregationSpec("Secret", CkRollupFunction.Avg, null, null),
                new CkRollupAggregationSpec("temperature_avg_sum", CkRollupFunction.Sum, null, null) // a source rollup column
            ]
        });

        Assert.False(await Sut().RevalidateAccessAsync(Rt));

        A.CallTo(() => _store.SetStatusAsync(Rt, CkArchiveStatus.Failed, A<string>.That.Contains("'Secret'"),
            A<IReadOnlyList<string>>.That.IsEmpty())).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Redact_RetriedAfterADefinitionWriteFailed_Converges()
    {
        Stub(CkArchiveStatus.Disabled, "Temperature", "Secret");
        A.CallTo(() => _store.EnumerateAsync()).Returns(AsyncEnumerable.Empty<ArchiveSnapshot>());
        A.CallTo(() => _store.RemoveIngestedColumnsAsync(Rt, A<IReadOnlyCollection<string>>._))
            .Throws(new InvalidOperationException("mongo down")).Once();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().RedactHiddenColumnsAsync(Rt));
        var result = await Sut().RedactHiddenColumnsAsync(Rt);

        Assert.Equal(["Secret"], result.RemovedColumns);
        A.CallTo(() => _repo.DropIngestedColumnStorageAsync(A<ArchiveSnapshot>._, A<IReadOnlyCollection<string>>._,
            A<System.Threading.CancellationToken>._)).MustHaveHappenedTwiceExactly();
        A.CallTo(() => _store.RemoveIngestedColumnsAsync(Rt, A<IReadOnlyCollection<string>>._)).MustHaveHappenedTwiceExactly();
    }

    [Fact]
    public async Task Redact_WithoutAHiddenColumn_ChangesNothing()
    {
        Stub(CkArchiveStatus.Activated, "Temperature", "Reading.Value");

        var result = await Sut().RedactHiddenColumnsAsync(Rt);

        Assert.Empty(result.RemovedColumns);
        Assert.Equal(CkArchiveStatus.Activated, result.Status);
        A.CallTo(() => _repo.DropIngestedColumnStorageAsync(A<ArchiveSnapshot>._, A<IReadOnlyCollection<string>>._,
            A<System.Threading.CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => _audit.RecordTransitionAsync(A<string>._, A<OctoObjectId>._, A<CkArchiveStatus>._,
            A<CkArchiveStatus>._, A<string?>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Redact_IsRefusedWhileARollupAggregatesAHiddenColumn()
    {
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Snapshot(CkArchiveStatus.Failed, _sensor, ["Secret"], "Secret"));
        var rollupRt = OctoObjectId.GenerateNewId();
        var otherRollupRt = OctoObjectId.GenerateNewId();
        var aggregations = new List<CkRollupAggregationSpec> { new("secret", CkRollupFunction.Avg, null, null) };
        A.CallTo(() => _store.EnumerateAsync()).Returns(new[]
        {
            new ArchiveSnapshot(rollupRt, _sensor, CkArchiveStatus.Activated, null, []) { RollupAggregations = aggregations },
            new ArchiveSnapshot(otherRollupRt, _sensor, CkArchiveStatus.Activated, null, []) { RollupAggregations = aggregations }
        }.ToAsyncEnumerable());
        A.CallTo(() => _rollups.GetAsync(rollupRt)).Returns(new RollupArchiveSnapshot(rollupRt, _sensor,
            CkArchiveStatus.Activated, null, [new RollupSourceReference(Rt)], TimeSpan.FromHours(1), TimeSpan.Zero,
            null, aggregations, null));
        // A rollup of another source is no dependent.
        A.CallTo(() => _rollups.GetAsync(otherRollupRt)).Returns(new RollupArchiveSnapshot(otherRollupRt, _sensor,
            CkArchiveStatus.Activated, null, [new RollupSourceReference(OctoObjectId.GenerateNewId())],
            TimeSpan.FromHours(1), TimeSpan.Zero, null, aggregations, null));

        var ex = await Assert.ThrowsAsync<HiddenColumnRedactionBlockedException>(() => Sut().RedactHiddenColumnsAsync(Rt));

        Assert.Equal([rollupRt], ex.DependentRollupRtIds);
        A.CallTo(() => _repo.DropIngestedColumnStorageAsync(A<ArchiveSnapshot>._, A<IReadOnlyCollection<string>>._,
            A<System.Threading.CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => _store.RemoveIngestedColumnsAsync(A<OctoObjectId>._, A<IReadOnlyCollection<string>>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Redact_WithoutACkCache_IsRefused()
    {
        Stub(CkArchiveStatus.Failed, "Secret");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut(withCache: false).RedactHiddenColumnsAsync(Rt));
    }

    // ---------- Redaction against the columns the caller confirmed ----------------------------------------------------

    [Fact]
    public async Task RedactConfirmed_WithExactlyTheHiddenColumns_Drops_IgnoringCaseOrderAndDuplicates()
    {
        A.CallTo(() => _store.GetAsync(Rt)).ReturnsNextFromSequence(
            Snapshot(CkArchiveStatus.Failed, _sensor, ["Secret", "Reading"], "Temperature", "Secret", "Reading"),
            Snapshot(CkArchiveStatus.Failed, _sensor, ["Secret", "Reading"], "Temperature"));
        A.CallTo(() => _store.EnumerateAsync()).Returns(AsyncEnumerable.Empty<ArchiveSnapshot>());

        var result = await Sut().RedactConfirmedHiddenColumnsAsync(Rt, ["reading", "SECRET", "Secret"]);
        A.CallTo(() => _audit.RecordTransitionAsync(TenantId, Rt, CkArchiveStatus.Failed, CkArchiveStatus.Failed,
            A<string>.That.EndsWith("(confirmed by the caller)"))).MustHaveHappenedOnceExactly();

        Assert.Equal(CkArchiveStatus.Activated, result.Status);
        Assert.Equal(["Secret", "Reading"], result.RemovedColumns);
        A.CallTo(() => _repo.DropIngestedColumnStorageAsync(A<ArchiveSnapshot>._,
                A<IReadOnlyCollection<string>>.That.IsSameSequenceAs(new[] { "Secret", "Reading" }),
                A<System.Threading.CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _store.RemoveIngestedColumnsAsync(Rt,
            A<IReadOnlyCollection<string>>.That.IsSameSequenceAs(new[] { "Secret", "Reading" }))).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task RedactConfirmed_WhenAnotherColumnBecameHiddenSinceTheRead_IsRefused_AndNothingIsDropped()
    {
        // The caller saw only Secret; a later model change made Reading Hidden as well.
        A.CallTo(() => _store.GetAsync(Rt)).Returns(
            Snapshot(CkArchiveStatus.Failed, _sensor, ["Secret"], "Temperature", "Secret", "Reading"));

        var ex = await Assert.ThrowsAsync<HiddenColumnRedactionColumnsChangedException>(
            () => Sut().RedactConfirmedHiddenColumnsAsync(Rt, ["Secret"]));

        Assert.Equal(["Secret"], ex.ExpectedColumns);
        Assert.Equal(["Secret", "Reading"], ex.CurrentColumns);
        Assert.Equal(Rt, ex.ArchiveRtId);
        AssertNothingWasRedacted();
    }

    [Fact]
    public async Task RedactConfirmed_WhenAConfirmedColumnIsNoLongerHidden_IsRefused_AndNothingIsDropped()
    {
        A.CallTo(() => _store.GetAsync(Rt)).Returns(
            Snapshot(CkArchiveStatus.Failed, _sensor, ["Secret", "Temperature"], "Temperature", "Secret"));

        var ex = await Assert.ThrowsAsync<HiddenColumnRedactionColumnsChangedException>(
            () => Sut().RedactConfirmedHiddenColumnsAsync(Rt, ["Secret", "Temperature"]));

        Assert.Equal(["Secret"], ex.CurrentColumns);
        AssertNothingWasRedacted();
    }

    [Fact]
    public async Task RedactConfirmed_WithAnUnknownColumn_IsRefused_AndNothingIsDropped()
    {
        A.CallTo(() => _store.GetAsync(Rt)).Returns(
            Snapshot(CkArchiveStatus.Failed, _sensor, ["Secret"], "Temperature", "Secret"));

        var ex = await Assert.ThrowsAsync<HiddenColumnRedactionColumnsChangedException>(
            () => Sut().RedactConfirmedHiddenColumnsAsync(Rt, ["Secret", "NoSuchColumn"]));

        Assert.Equal(["Secret", "NoSuchColumn"], ex.ExpectedColumns);
        Assert.Equal(["Secret"], ex.CurrentColumns);
        AssertNothingWasRedacted();
    }

    [Fact]
    public async Task RedactConfirmed_WithAnEmptyList_WhileAColumnIsHidden_IsRefused_AndNothingIsDropped()
    {
        Stub(CkArchiveStatus.Disabled, "Temperature", "Secret");

        var ex = await Assert.ThrowsAsync<HiddenColumnRedactionColumnsChangedException>(
            () => Sut().RedactConfirmedHiddenColumnsAsync(Rt, []));

        Assert.Empty(ex.ExpectedColumns);
        Assert.Equal(["Secret"], ex.CurrentColumns);
        AssertNothingWasRedacted();
    }

    [Fact]
    public async Task RedactConfirmed_RepeatedAfterASuccess_IsRefused_BecauseNoColumnIsHiddenAnyMore()
    {
        Stub(CkArchiveStatus.Activated, "Temperature");

        var ex = await Assert.ThrowsAsync<HiddenColumnRedactionColumnsChangedException>(
            () => Sut().RedactConfirmedHiddenColumnsAsync(Rt, ["Secret"]));

        Assert.Empty(ex.CurrentColumns);
        AssertNothingWasRedacted();
    }

    [Fact]
    public async Task RedactConfirmed_WithAnEmptyList_AndNoHiddenColumn_ChangesNothing()
    {
        Stub(CkArchiveStatus.Activated, "Temperature", "Reading.Value");

        var result = await Sut().RedactConfirmedHiddenColumnsAsync(Rt, []);

        Assert.Empty(result.RemovedColumns);
        Assert.Equal(CkArchiveStatus.Activated, result.Status);
        AssertNothingWasRedacted();
    }

    [Fact]
    public async Task RedactConfirmed_IsCheckedBeforeTheActivatedArchiveRefusal()
    {
        // The changed set is the more precise answer for the caller: it must read the archive again in any case.
        Stub(CkArchiveStatus.Activated, "Temperature", "Secret");

        await Assert.ThrowsAsync<HiddenColumnRedactionColumnsChangedException>(
            () => Sut().RedactConfirmedHiddenColumnsAsync(Rt, ["Temperature"]));

        AssertNothingWasRedacted();
    }

    /// <summary>No audit entry, no dropped storage, no definition change and no status write.</summary>
    private void AssertNothingWasRedacted()
    {
        A.CallTo(() => _audit.RecordTransitionAsync(A<string>._, A<OctoObjectId>._, A<CkArchiveStatus>._,
            A<CkArchiveStatus>._, A<string?>._)).MustNotHaveHappened();
        A.CallTo(() => _repo.DropIngestedColumnStorageAsync(A<ArchiveSnapshot>._, A<IReadOnlyCollection<string>>._,
            A<System.Threading.CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => _store.RemoveIngestedColumnsAsync(A<OctoObjectId>._, A<IReadOnlyCollection<string>>._))
            .MustNotHaveHappened();
        A.CallTo(() => _store.SetStatusAsync(A<OctoObjectId>._, A<CkArchiveStatus>._)).MustNotHaveHappened();
        A.CallTo(() => _store.SetStatusAsync(A<OctoObjectId>._, A<CkArchiveStatus>._, A<string?>._,
            A<IReadOnlyList<string>>._)).MustNotHaveHappened();
    }

    [Fact]
    public void Snapshot_WithoutHiddenColumns_HidesThemFromTheQueryPaths_CaseInsensitive()
    {
        var snapshot = Snapshot(CkArchiveStatus.Failed, _sensor, ["secret"], "Temperature", "Secret") with
        {
            Columns =
            [
                new CkArchiveColumnSpec("Temperature", true, false), new CkArchiveColumnSpec("Secret", true, false),
                new CkArchiveColumnSpec(string.Empty, false, false) { Name = "secret", Formula = "Temperature * 2" }
            ]
        };

        Assert.True(snapshot.IsFailedOnHiddenColumns);
        var visible = snapshot.WithoutHiddenColumns();

        Assert.Equal(["Temperature", ""], visible.Columns.Select(c => c.Path));
        var plain = Snapshot(CkArchiveStatus.Activated, _sensor, [], "Temperature");
        Assert.Same(plain, plain.WithoutHiddenColumns());
        Assert.False(plain.IsFailedOnHiddenColumns);
    }

    [Fact]
    public void Guard_NamesTheHiddenSubAttribute()
    {
        Assert.Equal("Wrapper.Inner.Calibration",
            ArchiveHiddenColumnGuard.FindHiddenAttribute(_cache, TenantId, _sensor, "wrapper"));
        Assert.Null(ArchiveHiddenColumnGuard.FindHiddenAttribute(_cache, TenantId, _sensor, "Wrapper.Label"));
    }
}
