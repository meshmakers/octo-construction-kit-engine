using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers.RangeRetention;

/// <summary>
///     The one-major-per-range rule of range retention (review H6, AB#5664), in one place: a compile with range retention
///     stores references major-qualified (<c>System@2/Entity-1</c>), so every dependency range of a model must stay inside the
///     major of its floor. <c>System-[2.5,3.0)</c> is fine; an unbounded range (<c>Basic-[2.4,)</c>, a bare <c>System</c> =
///     <c>&gt;=1.0.0</c>), <c>[2.5,3.0]</c> and <c>[2.5,4.0)</c> are rejected. Used by <c>CatalogModelResolver</c> (the compiler) and
///     by the range audit (<c>octo-ckc -c AuditRanges</c>), so the audit can never disagree with what the compiler enforces.
/// </summary>
public static class CkDependencyRangeRule
{
    /// <summary>
    ///     The floor of a declared range: its lower bound; an exclusive lower bound gets the next patch so the floor is inside
    ///     the range (review L13); no lower bound means 0.0.0.
    /// </summary>
    public static CkVersion FloorOf(CkVersionRange range)
    {
        var min = range.MinVersion ?? new CkVersion(0, 0, 0);
        return range.MinInclusive || range.MinVersion == null
            ? min
            : new CkVersion(min.Major, min.Minor, min.Revision + 1);
    }

    /// <summary>
    ///     True when every version the range admits at or above <paramref name="floor" /> has the floor's major.
    /// </summary>
    public static bool IsWithinOneMajor(CkVersionRange range, CkVersion floor)
    {
        var nextMajor = new CkVersion(floor.Major + 1, 0, 0);
        if (range.MaxVersion == null)
        {
            return false;
        }

        var comparison = range.MaxVersion.Value.CompareTo(nextMajor);
        return comparison < 0 || (comparison == 0 && !range.MaxInclusive);
    }

    /// <summary>
    ///     True when the declared dependency range stays inside one major (floor = <see cref="FloorOf" />).
    /// </summary>
    public static bool IsWithinOneMajor(CkModelIdVersionRange range) =>
        IsWithinOneMajor(range.ModelVersionRange, FloorOf(range.ModelVersionRange));
}
