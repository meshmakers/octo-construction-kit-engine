using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Formulas;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

/// <summary>
/// AB#7997: adding an ingested column to an activated archive. Storage first, definition last; idempotent; refused on a
/// non-activated archive, a rollup, an empty path or a name a computed column already uses.
/// </summary>
public class ArchiveLifecycleServiceAddColumnTests
{
    private const string TenantId = "tenant-x";
    private static readonly OctoObjectId Rt = OctoObjectId.GenerateNewId();
    private static readonly RtCkId<CkTypeId> TargetType = new("Test", new CkTypeId("Battery"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly InMemoryArchiveRuntimeStore _inner = new();
    private readonly IArchiveRuntimeStore _store;
    private readonly IStreamDataRepository _repo = A.Fake<IStreamDataRepository>();

    public ArchiveLifecycleServiceAddColumnTests()
    {
        _store = A.Fake<IArchiveRuntimeStore>(o => o.Wrapping(_inner));
    }

    private ArchiveLifecycleService NewSut() =>
        new(TenantId, _store, _repo, A.Fake<IArchiveAuditTrail>(), NullLogger<ArchiveLifecycleService>.Instance);

    private void Stub(CkArchiveStatus status, params CkArchiveColumnSpec[] columns) =>
        _inner.Put(new ArchiveSnapshot(Rt, TargetType, status, "battery", columns));

    private static CkArchiveColumnSpec Ingested(string path) => new(path, Indexed: false, Required: false);

    [Fact]
    public async Task Add_ToActivatedArchive_AddsStorageFirstThenTheDefinition()
    {
        Stub(CkArchiveStatus.Activated, Ingested("StateOfCharge"), Ingested("ChargingPower"));

        var added = await NewSut().AddArchiveColumnAsync(Rt, " DischargingPower ", indexed: true, Ct);

        Assert.True(added);
        A.CallTo(() => _repo.AddIngestedColumnStorageAsync(
                A<ArchiveSnapshot>.That.Matches(s => s.Columns.Count == 3 && s.Columns[2].Path == "DischargingPower"),
                "DischargingPower", A<CancellationToken>._))
            .MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => _store.AddIngestedColumnAsync(Rt,
                    A<CkArchiveColumnSpec>.That.Matches(c =>
                        c.Path == "DischargingPower" && c.Indexed && !c.Required && !c.IsComputed)))
                .MustHaveHappenedOnceExactly());

        var columns = (await _inner.GetAsync(Rt))!.Columns;
        Assert.Equal(["StateOfCharge", "ChargingPower", "DischargingPower"], columns.Select(c => c.Path));
    }

    [Fact]
    public async Task Add_WhenTheStorageFails_LeavesTheDefinitionUntouched()
    {
        Stub(CkArchiveStatus.Activated, Ingested("StateOfCharge"));
        A.CallTo(() => _repo.AddIngestedColumnStorageAsync(A<ArchiveSnapshot>._, A<string>._, A<CancellationToken>._))
            .ThrowsAsync(new ArchivePathInvalidException(Rt, "Nope", "unknown attribute"));

        await Assert.ThrowsAsync<ArchivePathInvalidException>(() => NewSut().AddArchiveColumnAsync(Rt, "Nope", false, Ct));

        A.CallTo(() => _store.AddIngestedColumnAsync(A<OctoObjectId>._, A<CkArchiveColumnSpec>._)).MustNotHaveHappened();
        Assert.Single((await _inner.GetAsync(Rt))!.Columns);
    }

    [Theory]
    [InlineData("ChargingPower")]
    [InlineData("chargingpower")] // the physical name is what identifies the column
    public async Task Add_OfAColumnTheArchiveHas_IsANoOp(string path)
    {
        Stub(CkArchiveStatus.Activated, Ingested("ChargingPower"));

        var added = await NewSut().AddArchiveColumnAsync(Rt, path, false, Ct);

        Assert.False(added);
        A.CallTo(() => _repo.AddIngestedColumnStorageAsync(A<ArchiveSnapshot>._, A<string>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => _store.AddIngestedColumnAsync(A<OctoObjectId>._, A<CkArchiveColumnSpec>._)).MustNotHaveHappened();
    }

    [Theory]
    [InlineData(CkArchiveStatus.Created)]
    [InlineData(CkArchiveStatus.Disabled)]
    [InlineData(CkArchiveStatus.Failed)]
    public async Task Add_ToANonActivatedArchive_IsRefused(CkArchiveStatus status)
    {
        Stub(status, Ingested("ChargingPower"));

        await Assert.ThrowsAsync<InvalidArchiveStateTransitionException>(
            () => NewSut().AddArchiveColumnAsync(Rt, "DischargingPower", false, Ct));

        A.CallTo(() => _repo.AddIngestedColumnStorageAsync(A<ArchiveSnapshot>._, A<string>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Add_ToARollup_IsRefused()
    {
        _inner.Put(new ArchiveSnapshot(Rt, TargetType, CkArchiveStatus.Activated, "rollup", [Ingested("power_avg")])
        {
            RollupAggregations = [],
        });

        await Assert.ThrowsAsync<ArchivePathInvalidException>(() => NewSut().AddArchiveColumnAsync(Rt, "Power", false, Ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Add_WithoutAPath_IsRefused(string path)
    {
        Stub(CkArchiveStatus.Activated);

        await Assert.ThrowsAsync<ArchivePathInvalidException>(() => NewSut().AddArchiveColumnAsync(Rt, path, false, Ct));
    }

    [Fact]
    public async Task Add_WhoseNameAComputedColumnUses_IsRefused()
    {
        Stub(CkArchiveStatus.Activated, new CkArchiveColumnSpec(string.Empty, false, false)
        {
            Name = "dischargingpower",
            Formula = "chargingpower * -1",
            ResultType = FormulaResultType.Double,
            ComputedState = ComputedColumnState.Active,
        });

        var ex = await Assert.ThrowsAsync<ArchivePathInvalidException>(
            () => NewSut().AddArchiveColumnAsync(Rt, "DischargingPower", false, Ct));

        Assert.Contains("computed column", ex.Message);
    }

    [Fact]
    public async Task Add_OfAnUnknownArchive_Throws()
    {
        await Assert.ThrowsAsync<ArchiveNotFoundException>(
            () => NewSut().AddArchiveColumnAsync(OctoObjectId.GenerateNewId(), "DischargingPower", false, Ct));
    }
}
