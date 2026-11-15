using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// An immutable, sorted, duplicate-free set of rtIds (metering points / streams) that a late-delivery recompute is
/// restricted to (AB#9016). Compared by value, so the recompute records that carry one
/// (<see cref="ArchiveDirtyWindow.RtIds"/>, <see cref="ArchiveRecomputeRange.RtIdSet"/>,
/// <see cref="RecomputeJobSnapshot.RtIdSet"/>) keep their value semantics: a store removes an obligation by handing
/// back the record it read, and two sets with the same members are the same set whatever order they were built in.
/// </summary>
public sealed class RtIdSet : IReadOnlyCollection<OctoObjectId>, IEquatable<RtIdSet>
{
    private readonly OctoObjectId[] _members;

    private RtIdSet(OctoObjectId[] sortedDistinctMembers)
    {
        _members = sortedDistinctMembers;
    }

    /// <summary>
    /// The set of the given rtIds, or <c>null</c> when there are none: an empty set never stands for "no series" -
    /// a recompute without a set is the unscoped one, so an empty input carries no scope.
    /// </summary>
    public static RtIdSet? Of(IEnumerable<OctoObjectId>? rtIds)
    {
        if (rtIds is null)
        {
            return null;
        }

        var members = rtIds.Where(id => id != OctoObjectId.Empty).Distinct().Order().ToArray();
        return members.Length == 0 ? null : new RtIdSet(members);
    }

    /// <summary>
    /// The set parsed from stored rtId strings (the <c>StringArray</c> CK attributes), or <c>null</c> when there are
    /// none. Entries that are empty or no valid rtId are skipped.
    /// </summary>
    public static RtIdSet? Parse(IEnumerable<string?>? rtIds)
    {
        if (rtIds is null)
        {
            return null;
        }

        var parsed = new List<OctoObjectId>();
        foreach (var value in rtIds)
        {
            if (!string.IsNullOrWhiteSpace(value) && OctoObjectId.TryParse(value, out var rtId))
            {
                parsed.Add(rtId);
            }
        }

        return Of(parsed);
    }

    /// <summary>
    /// The union of the given sets, or <c>null</c> when one of them is <c>null</c> (no scope absorbs every scope) or
    /// none is given.
    /// </summary>
    public static RtIdSet? Union(IEnumerable<RtIdSet?> sets)
    {
        var members = new List<OctoObjectId>();
        var any = false;
        foreach (var set in sets)
        {
            if (set is null)
            {
                return null;
            }

            any = true;
            members.AddRange(set._members);
        }

        return any ? Of(members) : null;
    }

    /// <inheritdoc />
    public int Count => _members.Length;

    /// <summary>Whether <paramref name="rtId"/> is a member.</summary>
    public bool Contains(OctoObjectId rtId) => Array.BinarySearch(_members, rtId) >= 0;

    /// <summary>Whether every member of this set is also a member of <paramref name="other"/>.</summary>
    public bool IsSubsetOf(RtIdSet other) => _members.All(other.Contains);

    /// <summary>The members as the strings the CK attributes store, in sort order.</summary>
    public IReadOnlyList<string> ToStrings() => _members.Select(m => m.ToString()).ToList();

    /// <inheritdoc />
    public IEnumerator<OctoObjectId> GetEnumerator() => ((IEnumerable<OctoObjectId>)_members).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public bool Equals(RtIdSet? other) =>
        other is not null && (ReferenceEquals(this, other) || _members.AsSpan().SequenceEqual(other._members));

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as RtIdSet);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var member in _members)
        {
            hash.Add(member);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString() => string.Join(",", _members);
}
