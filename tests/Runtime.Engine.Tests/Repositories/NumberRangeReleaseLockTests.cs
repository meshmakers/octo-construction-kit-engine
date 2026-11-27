using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Repositories;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Repositories;

/// <summary>
///     Decision D11: once a number range with a release configuration has handed out its first number, no write may
///     unlock its documents. Removing <c>Release</c>, changing <c>StateAttribute</c> or <c>StornoCkTypeId</c>, removing a
///     released value, adding a changeable attribute and deleting the range are refused (message 168); adding a released
///     value and removing a changeable attribute are allowed; before the first number everything is allowed. Checked on
///     every write of the funnel and of the bulk import. A numbered range also keeps its counter at or after the first
///     number and its name.
/// </summary>
public class NumberRangeReleaseLockTests
{
    private readonly ReleasedDocumentTestModel _model = new();
    private readonly IOctoSession _session = A.Fake<IOctoSession>();

    private static RtRecord Release(IEnumerable<string> releasedValues, IEnumerable<string>? changeable = null,
        string stateAttribute = "DocumentState", string? storno = null)
    {
        var release = new RtRecord();
        release.SetAttributeRawValue(NumberRangeRelease.StateAttributeAttribute, stateAttribute);
        release.SetAttributeRawValue(NumberRangeRelease.ReleasedValuesAttribute, releasedValues.ToList());
        if (changeable != null)
        {
            release.SetAttributeRawValue(NumberRangeRelease.ChangeableAttributesAttribute, changeable.ToList());
        }

        if (storno != null)
        {
            release.SetAttributeRawValue(NumberRangeRelease.StornoCkTypeIdAttribute, storno);
        }

        return release;
    }

    private static RtEntity Range(long currentValue, RtRecord? release, long? startValue = null,
        IEnumerable<(int Year, long Value)>? periods = null)
    {
        var range = new RtEntity(new RtCkId<CkTypeId>("System/AutoIncrement"), OctoObjectId.GenerateNewId(),
            new Dictionary<string, object?> { ["CurrentValue"] = currentValue, ["Release"] = release });
        if (startValue != null)
        {
            range.SetAttributeRawValue("StartValue", startValue);
        }

        if (periods != null)
        {
            range.SetAttributeRawValue("Periods", periods.Select(p => new RtRecord(
                new RtCkId<CkRecordId>("System/AutoIncrementPeriod"),
                new Dictionary<string, object?> { ["FiscalYear"] = p.Year, ["CurrentValue"] = p.Value })).ToList());
        }

        return range;
    }

    private static RtEntity WithRelease(RtEntity range, RtRecord? release) =>
        new(range.GetRtCkTypeId(), range.RtId, new Dictionary<string, object?>(range.Attributes) { ["Release"] = release })
        {
            RtWellKnownName = range.RtWellKnownName
        };

    private static readonly RtRecord Stored = Release(["Released", "Cancelled"], ["PaymentStatus"],
        storno: "Doc/CreditNote");

    public static TheoryData<string, RtRecord?, string> RefusedChanges => new()
    {
        { "removes Release", null, "Release" },
        { "changes StateAttribute", Release(["Released", "Cancelled"], ["PaymentStatus"], "Status", "Doc/CreditNote"), "Release.StateAttribute" },
        { "changes StornoCkTypeId", Release(["Released", "Cancelled"], ["PaymentStatus"], storno: "Doc/Other"), "Release.StornoCkTypeId" },
        { "removes StornoCkTypeId", Release(["Released", "Cancelled"], ["PaymentStatus"]), "Release.StornoCkTypeId" },
        { "removes a released value", Release(["Released"], ["PaymentStatus"], storno: "Doc/CreditNote"), "Release.ReleasedValues" },
        { "adds a changeable attribute", Release(["Released", "Cancelled"], ["PaymentStatus", "Title"], storno: "Doc/CreditNote"), "Release.ChangeableAttributes" },
        { "leaves Release unreadable", Release([], ["PaymentStatus"], storno: "Doc/CreditNote"), "Release" }
    };

    [Theory]
    [MemberData(nameof(RefusedChanges))]
    public void NumberedRange_RefusesTheChange(string _, RtRecord? release, string change)
    {
        var stored = Range(3, Stored);

        Assert.Equal([change], NumberRangeReleaseLock.ForbiddenChanges(stored, WithRelease(stored, release)));
    }

    [Fact]
    public void NumberedRange_AllowsChangesThatLockMore()
    {
        var stored = Range(3, Stored);
        var stricter = Release(["Released", "Cancelled", "Archived"], [], storno: "Doc/CreditNote");

        Assert.Empty(NumberRangeReleaseLock.ForbiddenChanges(stored, WithRelease(stored, stricter)));
        Assert.Empty(NumberRangeReleaseLock.ForbiddenChanges(stored, WithRelease(stored, Stored)));
    }

    [Fact]
    public void NumberedRange_IsNotDeleted()
    {
        Assert.Equal([ReleasedDocumentLock.RtStateChange],
            NumberRangeReleaseLock.ForbiddenChanges(Range(3, Stored), null));
    }

    [Theory]
    [MemberData(nameof(RefusedChanges))]
    public void BeforeTheFirstNumber_EverythingIsConfigurable(string _, RtRecord? release, string refusedOnceNumbered)
    {
        Assert.NotEmpty(refusedOnceNumbered);
        foreach (var stored in new[]
                 {
                     Range(0, Stored), Range(1234, Stored, startValue: 1235),
                     Range(0, Stored, startValue: 5, periods: [(2026, 4)])
                 })
        {
            Assert.Empty(NumberRangeReleaseLock.ForbiddenChanges(stored, WithRelease(stored, release)));
            Assert.Empty(NumberRangeReleaseLock.ForbiddenChanges(stored, null));
        }
    }

    [Fact]
    public void ARangeHasNumbered_WhenItsCounterOrAPeriodReachedTheFirstNumber()
    {
        Assert.False(NumberRangeReleaseLock.HasNumbered(Range(0, Stored)));
        Assert.True(NumberRangeReleaseLock.HasNumbered(Range(1, Stored)));
        Assert.False(NumberRangeReleaseLock.HasNumbered(Range(1234, Stored, startValue: 1235)));
        Assert.True(NumberRangeReleaseLock.HasNumbered(Range(1235, Stored, startValue: 1235)));
        Assert.True(NumberRangeReleaseLock.HasNumbered(Range(0, Stored, periods: [(2026, 1)])));
        Assert.False(NumberRangeReleaseLock.HasNumbered(Range(0, Stored, startValue: 10, periods: [(2026, 9)])));
    }

    [Fact]
    public void ARangeWithoutRelease_IsFreelyConfigurable()
    {
        var stored = Range(5, null);

        Assert.Empty(NumberRangeReleaseLock.ForbiddenChanges(stored, WithRelease(stored, Stored)));
        Assert.Empty(NumberRangeReleaseLock.ForbiddenChanges(stored, null));
    }

    [Fact]
    public void AMemberPathWriteIntoRelease_IsRefusedOnANumberedRange()
    {
        var stored = Range(3, Stored);
        var partial = new RtEntity(stored.GetRtCkTypeId(), stored.RtId);
        partial.SetAttributeRawValue("Release.ReleasedValues", new List<string> { "Released" });

        Assert.Equal(["Release"],
            NumberRangeReleaseLock.ForbiddenChanges(stored, NumberRangeReleaseLock.Overlay(stored, partial)));
    }

    [Fact]
    public async Task TheWriteFunnel_RefusesAnUpdateThatRemovesARelease_AndAllowsTheCounterAlone()
    {
        var range = _model.StoreRange(currentValue: 3);
        var id = new RtEntityId(range.GetRtCkTypeId(), range.RtId);
        var removal = new RtEntity(range.GetRtCkTypeId(), range.RtId,
            new Dictionary<string, object?> { ["Release"] = null });

        var refused = await Assert.ThrowsAsync<ReleasedDocumentLockedException>(() =>
            ReleasedDocumentLock.CheckMutationAsync(_session, _model.DataSource, _model.Cache,
                new EntityRuleEngineResult<RtEntity>([], new Dictionary<RtEntityId, RtEntity> { [id] = removal }, [],
                    []), new GraphRuleEngineResult(), BulkRtMutationOptions.Default with
                {
                    DisablePreDocumentModifications = true
                }));
        var refusal = Assert.Single(refused.Refusals);
        Assert.Equal(["Release"], refusal.Changes);
        Assert.Equal(NumberRangeReleaseLock.NumberedState, refusal.State);
        Assert.Equal(168, Assert.Single(refused.OperationResult.Messages).MessageNumber);

        var counter = new RtEntity(range.GetRtCkTypeId(), range.RtId,
            new Dictionary<string, object?> { ["CurrentValue"] = 4L });
        await ReleasedDocumentLock.CheckMutationAsync(_session, _model.DataSource, _model.Cache,
            new EntityRuleEngineResult<RtEntity>([], new Dictionary<RtEntityId, RtEntity> { [id] = counter }, [], []),
            new GraphRuleEngineResult(), BulkRtMutationOptions.Default);
    }

    [Fact]
    public async Task TheWriteFunnel_RefusesTheDeleteAndTheReplaceOfANumberedRange()
    {
        var range = _model.StoreRange(currentValue: 3);
        var id = new RtEntityId(range.GetRtCkTypeId(), range.RtId);
        var replacement = WithRelease(range, null);

        await Assert.ThrowsAsync<ReleasedDocumentLockedException>(() => ReleasedDocumentLock.CheckMutationAsync(
            _session, _model.DataSource, _model.Cache, new EntityRuleEngineResult<RtEntity>([], [], [], [id]),
            new GraphRuleEngineResult(), BulkRtMutationOptions.Default));
        await Assert.ThrowsAsync<ReleasedDocumentLockedException>(() => ReleasedDocumentLock.CheckMutationAsync(
            _session, _model.DataSource, _model.Cache,
            new EntityRuleEngineResult<RtEntity>([], [], new Dictionary<RtEntityId, RtEntity> { [id] = replacement },
                []), new GraphRuleEngineResult(), BulkRtMutationOptions.Default));
    }

    private Task UpdateAsync(RtEntity range, Dictionary<string, object?> attributes, string? wellKnownName = null)
    {
        var id = new RtEntityId(range.GetRtCkTypeId(), range.RtId);
        var partial = new RtEntity(range.GetRtCkTypeId(), range.RtId, attributes) { RtWellKnownName = wellKnownName };
        return ReleasedDocumentLock.CheckMutationAsync(_session, _model.DataSource, _model.Cache,
            new EntityRuleEngineResult<RtEntity>([], new Dictionary<RtEntityId, RtEntity> { [id] = partial }, [], []),
            new GraphRuleEngineResult(), BulkRtMutationOptions.Default);
    }

    public static TheoryData<string, Dictionary<string, object?>, string> CounterResets => new()
    {
        { "lowers the counter to 0", new() { ["CurrentValue"] = 0L }, "CurrentValue" },
        { "raises the start value above the counter", new() { ["StartValue"] = 1_000_000_000L, ["End"] = 2_000_000_000L }, "StartValue" }
    };

    [Theory]
    [MemberData(nameof(CounterResets))]
    public async Task ANumberedRange_RefusesACounterReset_SoAReleaseRemovalStaysRefused(string _,
        Dictionary<string, object?> reset, string change)
    {
        var range = _model.StoreRange(currentValue: 3);

        // Step 1: the counter is not moved back before the first number.
        var refused = await Assert.ThrowsAsync<ReleasedDocumentLockedException>(() => UpdateAsync(range, reset));
        Assert.Equal([change], Assert.Single(refused.Refusals).Changes);

        // Step 2: so the range is still numbered and keeps its release configuration.
        await Assert.ThrowsAsync<ReleasedDocumentLockedException>(() =>
            UpdateAsync(range, new Dictionary<string, object?> { ["Release"] = null }));
    }

    [Fact]
    public void ANumberedRange_RefusesClearingItsPeriods_AndAllowsTheAllocatorsIncrement()
    {
        var stored = Range(0, Stored, startValue: 1, periods: [(2026, 4)]);
        var cleared = new RtEntity(stored.GetRtCkTypeId(), stored.RtId,
            new Dictionary<string, object?>(stored.Attributes) { ["Periods"] = new List<RtRecord>() });

        Assert.Equal(["Periods"], NumberRangeReleaseLock.ForbiddenChanges(stored, cleared));
        Assert.Empty(NumberRangeReleaseLock.ForbiddenChanges(stored, Range(0, Stored, startValue: 1, periods: [(2026, 5)])));
        Assert.Empty(NumberRangeReleaseLock.ForbiddenChanges(Range(3, Stored), Range(4, Stored)));
    }

    private static List<RtRecord> Periods(params (int Year, long Value)[] periods) =>
        periods.Select(p => new RtRecord(new RtCkId<CkRecordId>("System/AutoIncrementPeriod"),
            new Dictionary<string, object?> { ["FiscalYear"] = p.Year, ["CurrentValue"] = p.Value })).ToList();

    private static RtEntity With(RtEntity range, string attribute, object? value) =>
        new(range.GetRtCkTypeId(), range.RtId, new Dictionary<string, object?>(range.Attributes) { [attribute] = value })
        {
            RtWellKnownName = range.RtWellKnownName
        };

    public static TheoryData<string, string, object?, string> CounterMovesBack => new()
    {
        { "period counter 5 -> 1", "Periods", Periods((2026, 1), (2025, 9)), "Periods" },
        { "a period removed", "Periods", Periods((2026, 5)), "Periods" },
        { "CurrentValue 5 -> 3", "CurrentValue", 3L, "CurrentValue" },
        { "StartValue above the next number", "StartValue", 100L, "StartValue" }
    };

    [Theory]
    [MemberData(nameof(CounterMovesBack))]
    public void ANumberedRange_RefusesEveryWriteThatMovesACounterBack(string _, string attribute, object? value,
        string change)
    {
        var stored = Range(5, Stored, startValue: 1, periods: [(2026, 5), (2025, 9)]);

        Assert.Equal([change], NumberRangeReleaseLock.ForbiddenChanges(stored, With(stored, attribute, value)));
    }

    [Fact]
    public void ANumberedRange_AllowsTheCountersToMoveUp()
    {
        var stored = Range(5, Stored, startValue: 1, periods: [(2026, 5)]);

        Assert.Empty(NumberRangeReleaseLock.ForbiddenChanges(stored, With(stored, "CurrentValue", 6L)));
        Assert.Empty(NumberRangeReleaseLock.ForbiddenChanges(stored,
            With(stored, "Periods", Periods((2026, 6), (2027, 1)))));
        Assert.Empty(NumberRangeReleaseLock.ForbiddenChanges(stored, With(stored, "StartValue", 2L)));
    }

    /// <summary>
    ///     The bypass reproduced on the lane stack: (1) the period counter of a numbered range is set to 0, (2) Released is
    ///     removed from ReleasedValues, (3) a released invoice is changed. Step 1 is refused, so steps 2 and 3 stay refused.
    /// </summary>
    [Fact]
    public async Task TheThreeStepBypass_IsRefusedAtTheFirstStep()
    {
        var range = _model.StoreRange(currentValue: 0);
        range.SetAttributeRawValue("Periods", Periods((2026, 5)));
        var invoice = _model.StoreInvoice(ReleasedDocumentTestModel.Released);

        var step1 = await Assert.ThrowsAsync<ReleasedDocumentLockedException>(() =>
            UpdateAsync(range, new Dictionary<string, object?> { ["Periods"] = Periods((2026, 0)) }));
        Assert.Equal(["Periods"], Assert.Single(step1.Refusals).Changes);

        await Assert.ThrowsAsync<ReleasedDocumentLockedException>(() => UpdateAsync(range,
            new Dictionary<string, object?> { ["Release"] = Release(["Cancelled"]) }));

        var invoiceId = new RtEntityId(invoice.GetRtCkTypeId(), invoice.RtId);
        await Assert.ThrowsAsync<ReleasedDocumentLockedException>(() => ReleasedDocumentLock.CheckMutationAsync(
            _session, _model.DataSource, _model.Cache,
            new EntityRuleEngineResult<RtEntity>([], new Dictionary<RtEntityId, RtEntity>
            {
                [invoiceId] = _model.Partial(invoice, ("Amount", 1.0))
            }, [], []), new GraphRuleEngineResult(), BulkRtMutationOptions.Default));
    }

    [Fact]
    public async Task ANumberedRange_RefusesRenamingOrClearingItsName_OnUpdateReplaceAndImport()
    {
        var range = _model.StoreRange(currentValue: 3);
        var id = new RtEntityId(range.GetRtCkTypeId(), range.RtId);

        var renamed = await Assert.ThrowsAsync<ReleasedDocumentLockedException>(() =>
            UpdateAsync(range, new Dictionary<string, object?>(), "X"));
        Assert.Equal([NumberRangeReleaseLock.RtWellKnownNameChange], Assert.Single(renamed.Refusals).Changes);

        // A replace writes the whole document: without the name it clears it.
        var replacement = WithRelease(range, range.GetAttributeValueOrDefault("Release") as RtRecord);
        replacement.RtWellKnownName = null;
        await Assert.ThrowsAsync<ReleasedDocumentLockedException>(() => ReleasedDocumentLock.CheckMutationAsync(
            _session, _model.DataSource, _model.Cache,
            new EntityRuleEngineResult<RtEntity>([], [], new Dictionary<RtEntityId, RtEntity> { [id] = replacement },
                []), new GraphRuleEngineResult(), BulkRtMutationOptions.Default));

        var imported = WithRelease(range, range.GetAttributeValueOrDefault("Release") as RtRecord);
        imported.RtWellKnownName = "X";
        await Assert.ThrowsAsync<ReleasedDocumentLockedException>(() =>
            ReleasedDocumentLock.FilterImportAsync(_session, _model.DataSource, _model.Cache, [imported]));

        // A partial update without a name keeps it; one with the same name changes nothing.
        await UpdateAsync(range, new Dictionary<string, object?> { ["CurrentValue"] = 4L });
        await UpdateAsync(range, new Dictionary<string, object?>(), ReleasedDocumentTestModel.Range);
    }

    [Fact]
    public async Task TheImport_RefusesASeedThatAddsAChangeableAttribute_AndWritesOneThatKeepsTheConfiguration()
    {
        var range = _model.StoreRange(changeable: ["PaymentStatus"], currentValue: 3);
        var seeded = WithRelease(range, Release(["Released", "Cancelled"], ["PaymentStatus", "Title"]));

        var refused = await Assert.ThrowsAsync<ReleasedDocumentLockedException>(() =>
            ReleasedDocumentLock.FilterImportAsync(_session, _model.DataSource, _model.Cache, [seeded]));
        Assert.Equal(["Release.ChangeableAttributes"], Assert.Single(refused.Refusals).Changes);

        var unchanged = WithRelease(range, Release(["Released", "Cancelled"], ["PaymentStatus"]));
        Assert.Single(await ReleasedDocumentLock.FilterImportAsync(_session, _model.DataSource, _model.Cache,
            [unchanged]));
    }
}
