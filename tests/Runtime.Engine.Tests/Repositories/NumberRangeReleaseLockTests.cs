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
///     every write of the funnel and of the bulk import.
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
        new(range.GetRtCkTypeId(), range.RtId, new Dictionary<string, object?>(range.Attributes) { ["Release"] = release });

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
