using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>A change that needs an acknowledgement and has one.</summary>
/// <param name="Change">The classified change.</param>
/// <param name="Key">The change key (<see cref="CkChangeKey" />).</param>
/// <param name="Reason">The reason the author gave.</param>
public sealed record CkAcknowledgedChange(CkClassifiedModelChange Change, string Key, string Reason);

/// <summary>A change that needs an acknowledgement and has none.</summary>
/// <param name="Change">The classified change.</param>
/// <param name="Key">The change key (<see cref="CkChangeKey" />) the author has to put into <c>compatibility.acknowledge</c>.</param>
public sealed record CkUnacknowledgedChange(CkClassifiedModelChange Change, string Key);

/// <summary>An acknowledgement entry that matches no change of this release that needs one.</summary>
/// <param name="Change">The key as declared.</param>
/// <param name="Reason">The reason as declared.</param>
/// <param name="MatchedChange">
///     The change with exactly this key when there is one that needs no acknowledgement (an ordinary change; the
///     entry cannot waive anything), otherwise null.
/// </param>
public sealed record CkStaleAcknowledgement(string Change, string Reason, CkClassifiedModelChange? MatchedChange);

/// <summary>
///     The outcome of matching the <c>compatibility.acknowledge</c> entries of a model with the changes of a release
///     (AB#6295).
/// </summary>
public sealed record CkAcknowledgementResult
{
    /// <summary>No acknowledgement section and nothing to acknowledge (also: not enforced).</summary>
    public static readonly CkAcknowledgementResult None = new();

    /// <summary>
    ///     True when the acknowledge rule applies: ckLanguage 2 models. A ckLanguage 1 model cannot declare
    ///     acknowledgements; a change that "requires acknowledge" there is only advisory.
    /// </summary>
    public bool IsEnforced { get; init; }

    /// <summary>Changes that need an acknowledgement and have one, in diff order.</summary>
    public IReadOnlyList<CkAcknowledgedChange> Acknowledged { get; init; } = [];

    /// <summary>Changes that need an acknowledgement and have none (OCTO-CK203), in diff order.</summary>
    public IReadOnlyList<CkUnacknowledgedChange> Unacknowledged { get; init; } = [];

    /// <summary>Entries that match no change that needs one (OCTO-CK204), in declared order.</summary>
    public IReadOnlyList<CkStaleAcknowledgement> Stale { get; init; } = [];

    /// <summary>True when the acknowledge rule is violated.</summary>
    public bool HasFindings => Unacknowledged.Count > 0 || Stale.Count > 0;

    /// <summary>
    ///     Matches the entries with the changes. An acknowledgement never changes a level: it only decides whether a
    ///     change that needs one is accepted.
    /// </summary>
    /// <param name="changes">The classified changes of the release.</param>
    /// <param name="compatibility">The model's <c>compatibility</c> section, may be null.</param>
    /// <param name="isEnforced">True for a ckLanguage 2 model.</param>
    public static CkAcknowledgementResult Evaluate(IReadOnlyList<CkClassifiedModelChange> changes,
        CkCompatibilityDto? compatibility, bool isEnforced)
    {
        if (!isEnforced)
        {
            return None;
        }

        var entries = compatibility?.Acknowledge ?? [];
        var byKey = new Dictionary<string, CkAcknowledgeDto>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!byKey.ContainsKey(entry.Change))
            {
                byKey[entry.Change] = entry;
            }
        }

        var acknowledged = new List<CkAcknowledgedChange>();
        var unacknowledged = new List<CkUnacknowledgedChange>();
        var ordinaryByKey = new Dictionary<string, CkClassifiedModelChange>(StringComparer.Ordinal);
        var matchedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in changes)
        {
            var key = CkChangeKey.Of(change.Change);
            if (!change.RequiresAcknowledge)
            {
                if (!ordinaryByKey.ContainsKey(key))
                {
                    ordinaryByKey[key] = change;
                }

                continue;
            }

            if (byKey.TryGetValue(key, out var entry))
            {
                acknowledged.Add(new CkAcknowledgedChange(change, key, entry.Reason));
                matchedKeys.Add(key);
            }
            else
            {
                unacknowledged.Add(new CkUnacknowledgedChange(change, key));
            }
        }

        var stale = entries
            .Where(e => !matchedKeys.Contains(e.Change))
            .Select(e => new CkStaleAcknowledgement(e.Change, e.Reason,
                ordinaryByKey.TryGetValue(e.Change, out var ordinary) ? ordinary : null))
            .ToList();

        return new CkAcknowledgementResult
        {
            IsEnforced = true, Acknowledged = acknowledged, Unacknowledged = unacknowledged, Stale = stale
        };
    }
}
