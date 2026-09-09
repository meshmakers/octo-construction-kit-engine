using System;
using System.Collections.Generic;
using System.Linq;
using Meshmakers.Octo.Runtime.Contracts.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// Which rule of <see cref="RollupSourceColumnResolver.TryResolve"/> matched an aggregation spec
/// against one source of a rollup (AB#5157 §3).
/// </summary>
public enum RollupSourceColumnResolutionKind
{
    /// <summary>
    /// Rule 1: the source declares a column whose captured name equals the spec's
    /// <see cref="CkRollupAggregationSpec.SourcePath"/> verbatim — an ingested column by its
    /// <c>Path</c>, a computed column by its <c>Name</c>, or a rollup source's generated physical
    /// column addressed by its physical name (the pre-AB#5157 chained-rollup style). The
    /// aggregation function is applied directly to that single physical column.
    /// </summary>
    DeclaredColumn,

    /// <summary>
    /// Rule 2: the source is a rollup that stores the same logical aggregation — a child spec with
    /// the same function whose source path normalises to the same physical name as the parent's.
    /// The child's target column(s) are the parent's source column(s), read function-preserving
    /// (see <see cref="RollupSourceColumnResolver.TryResolve"/>).
    /// </summary>
    ChildAggregation,
}

/// <summary>
/// The outcome of resolving one aggregation spec against one source of a rollup
/// (<see cref="RollupSourceColumnResolver.TryResolve"/>).
/// </summary>
/// <param name="Kind">The rule that matched.</param>
/// <param name="ChildAggregation">
/// For <see cref="RollupSourceColumnResolutionKind.ChildAggregation"/> the matched spec of the
/// source rollup; its physical column(s) are
/// <see cref="RollupColumnGenerator.TargetColumnNamesFor"/> of this spec. <c>null</c> for
/// <see cref="RollupSourceColumnResolutionKind.DeclaredColumn"/>, where the physical column is the
/// storage mapping of the spec's own <see cref="CkRollupAggregationSpec.SourcePath"/>.
/// </param>
public sealed record RollupSourceColumnResolution(
    RollupSourceColumnResolutionKind Kind,
    CkRollupAggregationSpec? ChildAggregation)
{
    /// <summary>The rule-1 outcome: a declared column, no child spec involved.</summary>
    public static readonly RollupSourceColumnResolution Declared =
        new(RollupSourceColumnResolutionKind.DeclaredColumn, null);
}

/// <summary>
/// Resolves a rollup's <em>logical</em> aggregation specs (source path + function) into the
/// physical columns of one particular source (WI AB#5157 §3). Pure function, DB-neutral; the
/// activation validator uses it as the existence check (rule 14) and the CrateDB aggregation
/// layer uses the same outcome to build the per-source SQL.
/// </summary>
/// <remarks>
/// <para>
/// A rollup's specs name the <em>logical</em> path of the base attribute (<c>Amount.Value</c>).
/// A base archive (raw / time-range) captures that path verbatim as a column, so the function can
/// be applied to it directly. A rollup source, however, declares its <em>generated physical</em>
/// column names (<c>amountvalue_sum</c>): a single logical spec (<c>Amount.Value</c>, Sum) has to
/// be readable over a time-range base archive <em>and</em> over an hourly rollup of it — the AC1
/// cutover shape, where the legacy history and the native rollup serve different validity spans
/// of the same parent. Before this resolver the activation check required the verbatim path on
/// every source, which no mixed base + rollup rung could satisfy.
/// </para>
/// <para>
/// Chained rollups written before AB#5157 name the child's physical column and the function to
/// apply to it (<c>amountvalue_sum</c>, Sum). That style must keep working unchanged, which is
/// why rule 1 exists and wins.
/// </para>
/// </remarks>
public static class RollupSourceColumnResolver
{
    /// <summary>
    /// The physical-column mapping rule the CrateDB <c>ColumnNameMapper.PathToColumnName</c> and
    /// <see cref="RollupColumnGenerator"/> apply to a logical path: dots removed, lower-cased
    /// (<c>Amount.Value</c> ⇒ <c>amountvalue</c>). An already-physical name is returned unchanged,
    /// so two paths denote the same physical column exactly when their normalised forms are equal.
    /// </summary>
    /// <param name="path">A logical attribute path or an already-physical column name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <c>null</c>.</exception>
    public static string NormalisePath(string path) => RollupColumnGenerator.SanitisePath(path);

    /// <summary>
    /// The names an archive captures and a rollup spec can address verbatim (rule 1): an ingested
    /// column by its <c>Path</c>, a computed column by its <c>Name</c> (concept §10 / AB#4189). For
    /// a rollup source these are its generated physical column names. Ordinal comparison — the
    /// verbatim rule does not normalise.
    /// </summary>
    /// <param name="archive">The archive-level snapshot of the source.</param>
    public static IReadOnlySet<string> CapturedPaths(ArchiveSnapshot archive)
    {
        if (archive is null) throw new ArgumentNullException(nameof(archive));

        var sourcePaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in archive.Columns)
        {
            if (column.IsComputed)
            {
                if (!string.IsNullOrWhiteSpace(column.Name))
                {
                    sourcePaths.Add(column.Name);
                }
            }
            else if (!string.IsNullOrWhiteSpace(column.Path))
            {
                sourcePaths.Add(column.Path);
            }
        }

        return sourcePaths;
    }

    /// <summary>
    /// Resolves <paramref name="spec"/> against one source of the rollup, or returns <c>null</c>
    /// when the source cannot serve it (the activation validator then throws
    /// <see cref="RollupSourcePathMissingException"/> naming that source).
    /// </summary>
    /// <param name="spec">The parent rollup's logical aggregation spec.</param>
    /// <param name="source">
    /// The source's archive-level snapshot (its captured columns). For a rollup source these are
    /// the generated physical columns.
    /// </param>
    /// <param name="sourceRollup">
    /// The source's rollup snapshot when the source is a rollup (its own logical specs), otherwise
    /// <c>null</c>. Without it only rule 1 applies, so a logical spec over a rollup source whose
    /// rollup snapshot was not supplied does not resolve.
    /// </param>
    /// <returns>
    /// <see cref="RollupSourceColumnResolutionKind.DeclaredColumn"/> (rule 1) when
    /// <see cref="CapturedPaths"/> of <paramref name="source"/> contains
    /// <see cref="CkRollupAggregationSpec.SourcePath"/> verbatim — the pre-AB#5157 behaviour, which
    /// covers base archives, computed columns and physical-name chained specs; otherwise
    /// <see cref="RollupSourceColumnResolutionKind.ChildAggregation"/> (rule 2) with the first child
    /// spec of <paramref name="sourceRollup"/> whose <see cref="CkRollupAggregationSpec.Function"/>
    /// equals the parent's and whose <see cref="NormalisePath">normalised</see> source path equals
    /// the parent's normalised source path (and, for
    /// <see cref="CkRollupFunction.StateDuration"/>, whose
    /// <see cref="CkRollupAggregationSpec.ComparisonValue"/> is the same state literal); otherwise
    /// <c>null</c>. Rule 1 wins when both apply:
    /// a verbatim declared column is the more specific match and keeps a legacy chained spec
    /// reading exactly the column it names.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Under rule 2 the child's target column(s) — <see cref="RollupColumnGenerator.TargetColumnNamesFor"/>
    /// of the returned <see cref="RollupSourceColumnResolution.ChildAggregation"/> — are the
    /// parent's source column(s) and are read <em>function-preserving</em> over the child buckets
    /// inside each parent bucket (the CrateDB layer implements the SQL):
    /// </para>
    /// <list type="table">
    /// <listheader><term>Parent function</term><description>Child column(s) and how they are read</description></listheader>
    /// <item><term>Sum</term><description><c>{base}</c> — summed</description></item>
    /// <item><term>Count</term><description><c>{base}</c> — summed</description></item>
    /// <item><term>Avg</term><description><c>{base}_sum</c> and <c>{base}_count</c> — each summed (pair); the average is recomputed on read as <c>sum / NULLIF(count, 0)</c></description></item>
    /// <item><term>TimeWeightedAvg</term><description><c>{base}_integral</c> and <c>{base}_duration</c> — each summed (pair)</description></item>
    /// <item><term>StateDuration</term><description><c>{base}</c> — summed</description></item>
    /// <item><term>Min</term><description><c>{base}</c> — minimum</description></item>
    /// <item><term>Max</term><description><c>{base}</c> — maximum</description></item>
    /// <item><term>First</term><description><c>{base}</c> — the value of the earliest child window (by window order)</description></item>
    /// <item><term>Last</term><description><c>{base}</c> — the value of the latest child window (by window order)</description></item>
    /// </list>
    /// <para>
    /// The parent's function must equal the child's: a parent Avg over a child that only stores Sum
    /// cannot be recombined and is refused. The same-function requirement is what keeps the read
    /// function-preserving for every row of the table above.
    /// </para>
    /// </remarks>
    public static RollupSourceColumnResolution? TryResolve(
        CkRollupAggregationSpec spec, ArchiveSnapshot source, RollupArchiveSnapshot? sourceRollup)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (source is null) throw new ArgumentNullException(nameof(source));

        // Rule 1 — verbatim declared column. Wins over rule 2.
        if (CapturedPaths(source).Contains(spec.SourcePath))
        {
            return RollupSourceColumnResolution.Declared;
        }

        // Rule 2 — the same logical aggregation stored by a rollup source.
        if (sourceRollup?.Aggregations is not { } childSpecs)
        {
            return null;
        }

        var wanted = NormalisePath(spec.SourcePath);
        // The physical storage column(s) this logical spec generates (e.g. Amount.Value + Sum =>
        // "amountvalue_sum"). A pre-AB#5157 physically-chained rollup source names its own child's
        // physical column and pins the stored name with an explicit TargetColumnName, so its child
        // spec's generated column(s) equal ours even though its SourcePath is that physical name and
        // not the logical path — its normalised path therefore never equals ours. Matching on the
        // generated column(s) bridges a logical parent spec onto such a chained child so a new
        // (logical) rollup can be stacked on a seeded physical ladder (WI AB#5157 review: the sbeg
        // quarter rung over the seeded EC monthly rung).
        var parentColumns = RollupColumnGenerator.TargetColumnNamesFor(spec).ToList();
        foreach (var child in childSpecs)
        {
            if (child.Function != spec.Function || !ComparisonValueMatches(spec, child))
            {
                continue;
            }

            // (a) Logical twin: the child names the same logical path (both address the base
            // attribute). This is the all-logical cascade written by AB#5157.
            if (string.Equals(NormalisePath(child.SourcePath), wanted, StringComparison.Ordinal))
            {
                return new RollupSourceColumnResolution(RollupSourceColumnResolutionKind.ChildAggregation, child);
            }

            // (b) Physically-chained child: it stores exactly the physical column(s) this logical
            // spec maps to. Read them function-preserving, identically to (a). The Function guard
            // above keeps a name collision (a differently-aggregated column that happens to share the
            // name via TargetColumnName) from matching.
            if (parentColumns.Count > 0
                && parentColumns.SequenceEqual(
                    RollupColumnGenerator.TargetColumnNamesFor(child), StringComparer.Ordinal))
            {
                return new RollupSourceColumnResolution(RollupSourceColumnResolutionKind.ChildAggregation, child);
            }
        }

        return null;
    }

    /// <summary>
    /// A <see cref="CkRollupFunction.StateDuration"/> aggregation measures the time a column spent
    /// in one specific state, so a child that measured a <em>different</em> state is a different
    /// quantity and must not serve the parent (its column would otherwise be summed into the
    /// parent's, silently producing wrong durations). Every other function ignores
    /// <see cref="CkRollupAggregationSpec.ComparisonValue"/>.
    /// </summary>
    private static bool ComparisonValueMatches(CkRollupAggregationSpec spec, CkRollupAggregationSpec child)
    {
        if (spec.Function != CkRollupFunction.StateDuration)
        {
            return true;
        }

        return string.Equals(
            spec.ComparisonValue?.Trim(), child.ComparisonValue?.Trim(), StringComparison.Ordinal);
    }
}
