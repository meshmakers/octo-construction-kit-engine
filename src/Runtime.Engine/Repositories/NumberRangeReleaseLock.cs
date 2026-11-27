using System.Collections;
using System.Globalization;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
///     Decision D11 of the design note <c>docs/concept-number-ranges-and-released-documents.md</c>: once a number range
///     with a release configuration has handed out its first number, a write to the range must not unlock its documents.
///     A write to such a <c>System/AutoIncrement</c> entity is refused (message 168) when it removes <c>Release</c> (or
///     leaves it unreadable), changes <c>StateAttribute</c> or <c>StornoCkTypeId</c>, removes a value from
///     <c>ReleasedValues</c> or adds one to <c>ChangeableAttributes</c>, and the range itself is never deleted. Adding a
///     released value and removing a changeable attribute lock more and are allowed; before the first number everything
///     stays configurable. Called from the same places as <see cref="ReleasedDocumentLock" /> (every write of
///     <see cref="BulkRtMutation" /> and the bulk import), so no option and no identity skips it.
/// </summary>
internal static class NumberRangeReleaseLock
{
    internal const string CurrentValueAttribute = "CurrentValue";
    internal const string StartValueAttribute = "StartValue";
    internal const string PeriodsAttribute = "Periods";

    /// <summary>State shown in the refusal of a range write.</summary>
    internal const string NumberedState = "numbered";

    /// <summary>One write to a range: the target, the operation and the range as the write leaves it (null: deleted).</summary>
    internal sealed record RangeWrite(RtEntityId Target, string Operation, Func<RtEntity, RtEntity?> Resulting);

    /// <summary>
    ///     Refuses the writes of <paramref name="writes" /> that change the release configuration of a numbered range.
    ///     Reads only when a write targets a <c>System/AutoIncrement</c> entity.
    /// </summary>
    public static async Task CheckAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        ICkCacheService ckCacheService, IReadOnlyList<RangeWrite> writes)
    {
        var rangeWrites = writes.Where(w => w.Target.RtId != OctoObjectId.Empty && IsRange(w.Target.CkTypeId))
            .ToList();
        if (rangeWrites.Count == 0 ||
            !ckCacheService.TryGetRtCkType(repositoryDataSource.TenantId, ReleasedDocumentLock.AutoIncrementCkTypeId,
                out var rangeGraph))
        {
            return;
        }

        var ids = rangeWrites.Select(w => w.Target.RtId).Distinct().ToList();
        var stored = (await repositoryDataSource.GetRtCollection<RtEntity>(rangeGraph)
                .FindManyAsync(session, e => ids.Contains(e.RtId)).ConfigureAwait(false))
            .ToDictionary(e => e.RtId);

        var result = new OperationResult();
        var refusals = new List<ReleasedDocumentRefusal>();
        foreach (var write in rangeWrites)
        {
            if (!stored.TryGetValue(write.Target.RtId, out var range))
            {
                continue;
            }

            var changes = ForbiddenChanges(range, write.Resulting(range));
            if (changes.Count == 0)
            {
                continue;
            }

            var id = new RtEntityId(range.CkTypeId ?? write.Target.CkTypeId, range.RtId);
            var name = range.RtWellKnownName ?? range.RtId.ToString();
            refusals.Add(new ReleasedDocumentRefusal(id, write.Operation, changes, NumberedState, name));
            NumberRangeRelease.TryCreate(range, out var release, out _);
            result.AddMessage(MessageCodes.RtReleasedDocumentLocked($"{id.CkTypeId.FullName}@{id.RtId}", id.RtId,
                id.CkTypeId.FullName, NumberedState, name, write.Operation, string.Join(", ", changes),
                string.Join(", ", (release?.ChangeableAttributes ?? []).Order(StringComparer.Ordinal))));
        }

        if (refusals.Count > 0)
        {
            throw new ReleasedDocumentLockedException(result, refusals);
        }
    }

    /// <summary>
    ///     The changes of the release configuration from <paramref name="stored" /> to <paramref name="resulting" /> that
    ///     D11 refuses; empty when the stored range has no readable release configuration or has not numbered yet.
    ///     <paramref name="resulting" /> null is a delete.
    /// </summary>
    public static List<string> ForbiddenChanges(RtEntity stored, RtEntity? resulting)
    {
        var changes = new List<string>();
        if (stored.GetAttributeValueOrDefault(NumberRangeRelease.ReleaseAttribute) == null ||
            !NumberRangeRelease.TryCreate(stored, out var before, out _) || before == null || !HasNumbered(stored))
        {
            return changes;
        }

        if (resulting == null)
        {
            changes.Add(ReleasedDocumentLock.RtStateChange);
            return changes;
        }

        if (!NumberRangeRelease.TryCreate(resulting, out var after, out _) || after == null)
        {
            changes.Add(NumberRangeRelease.ReleaseAttribute);
            return changes;
        }

        if (!string.Equals(before.StateAttribute, after.StateAttribute, StringComparison.Ordinal))
        {
            changes.Add(Member(NumberRangeRelease.StateAttributeAttribute));
        }

        if (!string.Equals(StornoCkTypeIdOf(stored), StornoCkTypeIdOf(resulting), StringComparison.Ordinal))
        {
            changes.Add(Member(NumberRangeRelease.StornoCkTypeIdAttribute));
        }

        if (before.ReleasedValues.Except(after.ReleasedValues, StringComparer.Ordinal).Any())
        {
            changes.Add(Member(NumberRangeRelease.ReleasedValuesAttribute));
        }

        if (AddsChangeableAttributes(before, after))
        {
            changes.Add(Member(NumberRangeRelease.ChangeableAttributesAttribute));
        }

        return changes;
    }

    /// <summary>
    ///     The strict rule on <c>ChangeableAttributes</c> (decided by the developer): after the first number no attribute
    ///     may become changeable. Kept on its own so that it can be dropped without touching the other rules.
    /// </summary>
    private static bool AddsChangeableAttributes(NumberRangeRelease before, NumberRangeRelease after) =>
        after.ChangeableAttributes.Except(before.ChangeableAttributes, StringComparer.Ordinal).Any();

    /// <summary>
    ///     True when the range has handed out a number: a period whose counter reached the first number of a period
    ///     (<c>StartValue</c>, or 1), or a counter that reached <c>StartValue</c> - without a start value any counter above
    ///     0, which also counts a counter configured above 0 as numbered (fail closed).
    /// </summary>
    public static bool HasNumbered(RtEntity range)
    {
        var startValue = ToLong(range.GetAttributeValueOrDefault(StartValueAttribute));
        if (range.GetAttributeValueOrDefault(PeriodsAttribute) is IEnumerable periods and not string)
        {
            foreach (var period in periods)
            {
                if (period is RtTypeWithAttributes record &&
                    ToLong(record.GetAttributeValueOrDefault(CurrentValueAttribute)) is { } periodValue &&
                    periodValue >= (startValue ?? 1))
                {
                    return true;
                }
            }
        }

        var currentValue = ToLong(range.GetAttributeValueOrDefault(CurrentValueAttribute)) ?? 0;
        return startValue is { } start ? currentValue >= start : currentValue > 0;
    }

    /// <summary>
    ///     The range as a partial update leaves it: the stored attributes with the written ones laid over them. A member
    ///     path into <c>Release</c> cannot be laid over; it leaves the configuration unreadable, so it is refused on a
    ///     numbered range.
    /// </summary>
    public static RtEntity Overlay(RtEntity stored, RtEntity partial)
    {
        var resulting = new RtEntity(stored.GetRtCkTypeId(), stored.RtId, stored.Attributes);
        foreach (var (name, value) in partial.Attributes)
        {
            if (name.StartsWith(NumberRangeRelease.ReleaseAttribute + ".", StringComparison.Ordinal))
            {
                resulting.SetAttributeRawValue(NumberRangeRelease.ReleaseAttribute, "member path write");
                continue;
            }

            resulting.SetAttributeRawValue(name, value);
        }

        return resulting;
    }

    private static bool IsRange(RtCkId<CkTypeId>? ckTypeId) =>
        ckTypeId != null && string.Equals(ckTypeId.FullName, ReleasedDocumentLock.AutoIncrementCkTypeId.FullName,
            StringComparison.Ordinal);

    private static string Member(string name) => $"{NumberRangeRelease.ReleaseAttribute}.{name}";

    private static string? StornoCkTypeIdOf(RtEntity range) =>
        (range.GetAttributeValueOrDefault(NumberRangeRelease.ReleaseAttribute) as RtTypeWithAttributes)
        ?.GetAttributeValueOrDefault(NumberRangeRelease.StornoCkTypeIdAttribute) as string;

    private static long? ToLong(object? value)
    {
        try
        {
            return value == null ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }
}
