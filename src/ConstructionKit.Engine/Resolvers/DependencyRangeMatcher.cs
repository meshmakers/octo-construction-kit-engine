using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
///     Finds the queued dependency range a child range of a resolved dependency joins (review G3 E-H1).
///     <list type="bullet">
///         <item>
///             A child of a <b>classic</b> (exact-pinned) model joins as on main: by
///             <see cref="CkModelIdVersionRange" /> equality, which is "overlaps" — the exact pin
///             <c>System-[2.5.0]</c> merges into the declared <c>System-[2.5,3.0)</c> and the model compiles against
///             the highest version of the range. Entries queued by range-retaining models are not candidates, so a
///             graph without range retention behaves exactly like main.
///         </item>
///         <item>
///             A child of a <b>range-retaining</b> model joins structurally (same name and identical range): overlap
///             is not transitive, and with range retention <c>System-[2.5,3.0)</c> overlaps the pins
///             <c>System-[2.5.0]</c> and <c>System-[2.6.0]</c> at once (D2).
///         </item>
///     </list>
/// </summary>
internal sealed class DependencyRangeMatcher
{
    private readonly HashSet<Tuple<List<CkModelId>, CkModelIdVersionRange>> _rangeRetainingEntries = [];

    /// <summary>
    ///     Creates the matcher for the root ranges of a resolve. Root ranges of a range-retaining root (compile with
    ///     the flag on, or the hard resolve of a range-retaining compiled model — passed as
    ///     <see cref="RangeRetainingDependencyRanges" />) are matched structurally, like any range-retaining range.
    /// </summary>
    public DependencyRangeMatcher(ICollection<CkModelIdVersionRange> rootRanges,
        IEnumerable<Tuple<List<CkModelId>, CkModelIdVersionRange>> rootEntries)
    {
        if (rootRanges is RangeRetainingDependencyRanges)
        {
            foreach (var entry in rootEntries)
            {
                _rangeRetainingEntries.Add(entry);
            }
        }
    }

    public void Add(List<Tuple<List<CkModelId>, CkModelIdVersionRange>> dependencies, CkModelId origin,
        CkModelIdVersionRange childRange, bool childIsRangeRetaining)
    {
        var match = childIsRangeRetaining
            ? dependencies.FirstOrDefault(d => d.Item2.Name == childRange.Name &&
                                               d.Item2.ModelVersionRange.Equals(childRange.ModelVersionRange))
            : dependencies.Where(d => !_rangeRetainingEntries.Contains(d))
                .SingleOrDefault(d => d.Item2 == childRange);
        if (match != null)
        {
            match.Item1.Add(origin);
            return;
        }

        var entry = new Tuple<List<CkModelId>, CkModelIdVersionRange>([origin], childRange);
        dependencies.Add(entry);
        if (childIsRangeRetaining)
        {
            _rangeRetainingEntries.Add(entry);
        }
    }
}

/// <summary>
///     Marks the root dependency ranges of a range-retaining root for <see cref="DependencyRangeMatcher" />.
/// </summary>
internal sealed class RangeRetainingDependencyRanges(IEnumerable<CkModelIdVersionRange> ranges)
    : List<CkModelIdVersionRange>(ranges)
{
    /// <summary>The ranges as a resolver root: marked when the root is range-retaining.</summary>
    public static List<CkModelIdVersionRange> For(IEnumerable<CkModelIdVersionRange> ranges, bool rangeRetaining) =>
        rangeRetaining ? new RangeRetainingDependencyRanges(ranges) : ranges.ToList();
}
