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
/// Computed-column add/remove orchestration on <see cref="ArchiveLifecycleService"/> (AB#4189
/// Phase 7, §8). Verifies the validate → persist(Pending) → ALTER → Backfilling → backfill →
/// Active sequence (the final state write is the atomic switch) and the failure path.
/// </summary>
public class ArchiveLifecycleServiceComputedColumnTests
{
    private const string TenantId = "tenant-x";
    private static readonly OctoObjectId Rt = OctoObjectId.GenerateNewId();
    private static readonly RtCkId<CkTypeId> TargetType = new("Test", new CkTypeId("EnergyMeter"));

    private readonly IArchiveRuntimeStore _store = A.Fake<IArchiveRuntimeStore>();
    private readonly IStreamDataRepository _repo = A.Fake<IStreamDataRepository>();
    private readonly IArchiveAuditTrail _audit = A.Fake<IArchiveAuditTrail>();

    public ArchiveLifecycleServiceComputedColumnTests()
    {
        // These tests are about the orchestration, not the logical→physical rewrite (AB#4779), and
        // their formulas are already physical. Model the real contract's identity case: a formula
        // that names no logical column comes back unchanged. Without this the fake would hand back a
        // dummy and every assertion downstream would be about the wrong string.
        A.CallTo(() => _repo.NormalizeComputedFormulaAsync(
                A<OctoObjectId>._, A<IReadOnlyList<CkArchiveColumnSpec>>._, A<string>._,
                A<CancellationToken>._))
            .ReturnsLazily((OctoObjectId _, IReadOnlyList<CkArchiveColumnSpec> _, string f,
                CancellationToken _) => f);
    }

    private ArchiveLifecycleService NewSut() =>
        new(TenantId, _store, _repo, _audit, NullLogger<ArchiveLifecycleService>.Instance);

    private static CkArchiveColumnSpec Comp(string name) =>
        new(string.Empty, Indexed: true, Required: false)
        {
            Name = name,
            Formula = "activepower / apparentpower",
            ResultType = FormulaResultType.Double,
            ComputedState = ComputedColumnState.Active,
        };

    private void Stub(CkArchiveStatus status, params CkArchiveColumnSpec[] columns) =>
        A.CallTo(() => _store.GetAsync(Rt))
            .Returns(new ArchiveSnapshot(Rt, TargetType, status, "energy", columns));

    [Fact]
    public async Task Add_ToActiveArchive_RunsValidateAddBackfillThenFlipsActive_InOrder()
    {
        Stub(CkArchiveStatus.Activated);

        await NewSut().AddComputedColumnAsync(Rt, "powerFactor", "activepower / apparentpower",
            FormulaResultType.Double, indexed: true);

        // Validate runs over the prospective set carrying the new column, before any persistence.
        A.CallTo(() => _repo.ValidateComputedColumnsAsync(Rt,
                A<IReadOnlyList<CkArchiveColumnSpec>>.That.Matches(cols => cols.Any(c => c.Name == "powerFactor")),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        // Persisted Pending first; the lifecycle owns the state, not the caller.
        A.CallTo(() => _store.AddComputedColumnAsync(Rt,
                A<CkArchiveColumnSpec>.That.Matches(c =>
                    c.Name == "powerFactor" && c.ComputedState == ComputedColumnState.Pending && c.ComputedVersion == 0)))
            .MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => _repo.AddComputedColumnStorageAsync(A<ArchiveSnapshot>._, "powerFactor", A<CancellationToken>._))
                .MustHaveHappenedOnceExactly())
            .Then(A.CallTo(() => _store.SetComputedColumnStateAsync(Rt, "powerFactor", ComputedColumnState.Backfilling))
                .MustHaveHappenedOnceExactly())
            .Then(A.CallTo(() => _repo.BackfillComputedColumnAsync(A<ArchiveSnapshot>._, "powerFactor", A<CancellationToken>._))
                .MustHaveHappenedOnceExactly())
            .Then(A.CallTo(() => _store.SetComputedColumnStateAsync(Rt, "powerFactor", ComputedColumnState.Active))
                .MustHaveHappenedOnceExactly());

        A.CallTo(() => _store.SetComputedColumnStateAsync(Rt, "powerFactor", ComputedColumnState.Failed))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Add_BackfillFails_MarksColumnFailed_AndRethrows()
    {
        Stub(CkArchiveStatus.Activated);
        A.CallTo(() => _repo.BackfillComputedColumnAsync(A<ArchiveSnapshot>._, "powerFactor", A<CancellationToken>._))
            .Throws(new InvalidOperationException("crate down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewSut().AddComputedColumnAsync(Rt, "powerFactor", "activepower / apparentpower",
                FormulaResultType.Double, indexed: true));

        A.CallTo(() => _store.SetComputedColumnStateAsync(Rt, "powerFactor", ComputedColumnState.Failed))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _store.SetComputedColumnStateAsync(Rt, "powerFactor", ComputedColumnState.Active))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Add_ToNonActiveArchive_Throws_AndPersistsNothing()
    {
        Stub(CkArchiveStatus.Created);

        await Assert.ThrowsAsync<InvalidArchiveStateTransitionException>(() =>
            NewSut().AddComputedColumnAsync(Rt, "powerFactor", "activepower / apparentpower",
                FormulaResultType.Double, indexed: true));

        A.CallTo(() => _store.AddComputedColumnAsync(A<OctoObjectId>._, A<CkArchiveColumnSpec>._)).MustNotHaveHappened();
        A.CallTo(() => _repo.AddComputedColumnStorageAsync(A<ArchiveSnapshot>._, A<string>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Add_ValidationFails_PersistsNothing()
    {
        Stub(CkArchiveStatus.Activated);
        A.CallTo(() => _repo.ValidateComputedColumnsAsync(Rt, A<IReadOnlyList<CkArchiveColumnSpec>>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("cyclic"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewSut().AddComputedColumnAsync(Rt, "powerFactor", "activepower / apparentpower",
                FormulaResultType.Double, indexed: true));

        A.CallTo(() => _store.AddComputedColumnAsync(A<OctoObjectId>._, A<CkArchiveColumnSpec>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Remove_ValidatesPostRemovalSet_ThenRemoves()
    {
        Stub(CkArchiveStatus.Activated, Comp("powerFactor"));

        await NewSut().RemoveComputedColumnAsync(Rt, "powerFactor");

        // The validated set must no longer contain the removed column (catches a dangling reference).
        A.CallTo(() => _repo.ValidateComputedColumnsAsync(Rt,
                A<IReadOnlyList<CkArchiveColumnSpec>>.That.Matches(cols => cols.All(c => c.Name != "powerFactor")),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _store.RemoveComputedColumnAsync(Rt, "powerFactor")).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Remove_UnknownColumn_IsNoOp()
    {
        Stub(CkArchiveStatus.Activated);

        await NewSut().RemoveComputedColumnAsync(Rt, "doesNotExist");

        A.CallTo(() => _repo.ValidateComputedColumnsAsync(A<OctoObjectId>._, A<IReadOnlyList<CkArchiveColumnSpec>>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => _store.RemoveComputedColumnAsync(A<OctoObjectId>._, A<string>._)).MustNotHaveHappened();
    }

    private const string OldFormula = "activepower / apparentpower";
    private const string NewFormula = "activepower / 2";

    [Fact]
    public async Task UpdateFormula_AddsPendingColumnBeforeMarking_ThenBackfillsAndSwaps_InOrder()
    {
        Stub(CkArchiveStatus.Activated, Comp("powerFactor"));

        await NewSut().UpdateComputedColumnFormulaAsync(Rt, "powerFactor", NewFormula);

        // Validation sees the prospective set with the target's formula replaced.
        A.CallTo(() => _repo.ValidateComputedColumnsAsync(Rt,
                A<IReadOnlyList<CkArchiveColumnSpec>>.That.Matches(cols =>
                    cols.Any(c => c.Name == "powerFactor" && c.Formula == NewFormula)),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly()
            // ALTER the pending column BEFORE marking PendingFormula (so dual-write never misses it).
            .Then(A.CallTo(() => _repo.AddPendingComputedColumnStorageAsync(A<ArchiveSnapshot>._, "powerFactor", A<CancellationToken>._))
                .MustHaveHappenedOnceExactly())
            .Then(A.CallTo(() => _store.SetPendingFormulaAsync(Rt, "powerFactor", NewFormula))
                .MustHaveHappenedOnceExactly())
            .Then(A.CallTo(() => _repo.BackfillComputedColumnAsync(A<ArchiveSnapshot>._, "powerFactor", A<CancellationToken>._))
                .MustHaveHappenedOnceExactly())
            // version 0 -> 1 swap is the atomic commit.
            .Then(A.CallTo(() => _store.SwapComputedColumnFormulaAsync(Rt, "powerFactor", NewFormula, 1))
                .MustHaveHappenedOnceExactly());

        A.CallTo(() => _store.ClearPendingFormulaAsync(A<OctoObjectId>._, A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task UpdateFormula_BackfillFails_ClearsPendingFormula_AndRethrows()
    {
        Stub(CkArchiveStatus.Activated, Comp("powerFactor"));
        A.CallTo(() => _repo.BackfillComputedColumnAsync(A<ArchiveSnapshot>._, "powerFactor", A<CancellationToken>._))
            .Throws(new InvalidOperationException("crate down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewSut().UpdateComputedColumnFormulaAsync(Rt, "powerFactor", NewFormula));

        A.CallTo(() => _store.ClearPendingFormulaAsync(Rt, "powerFactor")).MustHaveHappenedOnceExactly();
        A.CallTo(() => _store.SwapComputedColumnFormulaAsync(A<OctoObjectId>._, A<string>._, A<string>._, A<int>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task UpdateFormula_UnchangedFormula_IsNoOp()
    {
        Stub(CkArchiveStatus.Activated, Comp("powerFactor"));

        await NewSut().UpdateComputedColumnFormulaAsync(Rt, "powerFactor", OldFormula);

        A.CallTo(() => _repo.ValidateComputedColumnsAsync(A<OctoObjectId>._, A<IReadOnlyList<CkArchiveColumnSpec>>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => _store.SetPendingFormulaAsync(A<OctoObjectId>._, A<string>._, A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task UpdateFormula_UnknownColumn_Throws_AndTouchesNothing()
    {
        Stub(CkArchiveStatus.Activated, Comp("powerFactor"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewSut().UpdateComputedColumnFormulaAsync(Rt, "doesNotExist", NewFormula));

        A.CallTo(() => _store.SetPendingFormulaAsync(A<OctoObjectId>._, A<string>._, A<string>._)).MustNotHaveHappened();
    }

    // ---------------------------------------------------------- AB#4779: logical column names

    /// <summary>Rewrites the logical spelling a caller may use to the stored physical form.</summary>
    private void StubRewrite(string logical, string physical) =>
        A.CallTo(() => _repo.NormalizeComputedFormulaAsync(
                A<OctoObjectId>._, A<IReadOnlyList<CkArchiveColumnSpec>>._, logical,
                A<CancellationToken>._))
            .Returns(physical);

    [Fact]
    public async Task Add_PersistsTheRewrittenFormula_NotWhatTheCallerTyped()
    {
        Stub(CkArchiveStatus.Activated);
        StubRewrite("Amount.Value / 1000", "amountvalue / 1000");

        await NewSut().AddComputedColumnAsync(Rt, "power", "Amount.Value / 1000",
            FormulaResultType.Double, indexed: false);

        // Everything downstream — validation, ingest, backfill — only ever sees the physical form.
        A.CallTo(() => _store.AddComputedColumnAsync(Rt,
                A<CkArchiveColumnSpec>.That.Matches(c => c.Formula == "amountvalue / 1000")))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Add_ValidatesTheRewrittenFormula()
    {
        Stub(CkArchiveStatus.Activated);
        StubRewrite("Amount.Value", "amountvalue");

        await NewSut().AddComputedColumnAsync(Rt, "power", "Amount.Value",
            FormulaResultType.Double, indexed: false);

        A.CallTo(() => _repo.ValidateComputedColumnsAsync(Rt,
                A<IReadOnlyList<CkArchiveColumnSpec>>.That.Matches(
                    cols => cols.Any(c => c.Formula == "amountvalue")),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task UpdateFormula_SameFormulaInLogicalSpelling_IsANoOp()
    {
        // The regression this guards: the stored formula is physical, so comparing the caller's
        // logical spelling against it looks like a change. Re-saving the identical formula from the
        // UI would then version the column (powerFactor__v1, __v2 …), backfill it and swap the
        // pointer — repeatedly, for no change at all.
        Stub(CkArchiveStatus.Activated, Comp("powerFactor"));
        StubRewrite("ActivePower / ApparentPower", OldFormula);

        await NewSut().UpdateComputedColumnFormulaAsync(Rt, "powerFactor",
            "ActivePower / ApparentPower");

        A.CallTo(() => _repo.ValidateComputedColumnsAsync(A<OctoObjectId>._,
                A<IReadOnlyList<CkArchiveColumnSpec>>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => _store.SetPendingFormulaAsync(A<OctoObjectId>._, A<string>._, A<string>._))
            .MustNotHaveHappened();
        A.CallTo(() => _repo.AddPendingComputedColumnStorageAsync(
                A<ArchiveSnapshot>._, A<string>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task UpdateFormula_PersistsTheRewrittenFormulaAsPending()
    {
        Stub(CkArchiveStatus.Activated, Comp("powerFactor"));
        StubRewrite("Amount.Value * 2", "amountvalue * 2");

        await NewSut().UpdateComputedColumnFormulaAsync(Rt, "powerFactor", "Amount.Value * 2");

        A.CallTo(() => _store.SetPendingFormulaAsync(Rt, "powerFactor", "amountvalue * 2"))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task UpdateFormula_SwapCommitsTheRewrittenFormula_NotTheCallersSpelling()
    {
        // Field report: the panel showed "Amount.Value + 2" after a formula change while the values
        // still looked right. The swap is what becomes the column's stored Formula, and ingest
        // evaluates that on every new row — so committing the logical spelling left the backfilled
        // rows correct (they came from the pending formula) and made every row ingested afterwards
        // evaluate to NULL. Asserting only on SetPendingFormulaAsync is what let it through.
        Stub(CkArchiveStatus.Activated, Comp("powerFactor"));
        StubRewrite("Amount.Value * 2", "amountvalue * 2");

        await NewSut().UpdateComputedColumnFormulaAsync(Rt, "powerFactor", "Amount.Value * 2");

        A.CallTo(() => _store.SwapComputedColumnFormulaAsync(Rt, "powerFactor", "amountvalue * 2", 1))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _store.SwapComputedColumnFormulaAsync(
                A<OctoObjectId>._, A<string>._, "Amount.Value * 2", A<int>._))
            .MustNotHaveHappened();
    }
}
