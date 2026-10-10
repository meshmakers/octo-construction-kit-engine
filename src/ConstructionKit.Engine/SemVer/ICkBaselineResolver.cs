using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     Decides which published version a model is compared against (AB#5450). One shared, read-only resolver for
///     every compatibility gate: <c>ValidateVersion</c> today, the compile gate (AB#6294) and the publish gate (F2.3)
///     later, so all gates agree on the baseline.
/// </summary>
/// <remarks>
///     Rules (documented in <c>docs/ck-semver-rules.md</c>, "Baseline"):
///     <list type="number">
///         <item><b>Same major:</b> the newest version within <c>[major.0.0, major+1.0.0)</c> of the declared version.</item>
///         <item><b>New major:</b> when the declared major has no version yet, the newest version of the highest lower
///             major. Only a model without any version is a first publication.</item>
///         <item><b>Never the model under test (AB#5434):</b> entries of the local file-system catalog at or above the
///             declared version are never the baseline; they are reported as ignored. Remote entries equal to the
///             declared version stay valid baselines.</item>
///         <item><b>Read-only:</b> the resolver only reads catalogs; it never publishes or refreshes.</item>
///         <item><b>Visible:</b> the result names the baseline, its catalog, whether it is local and the ignored
///             local entries.</item>
///     </list>
/// </remarks>
public interface ICkBaselineResolver
{
    /// <summary>
    ///     Resolves the baseline of <paramref name="modelName" /> for the <paramref name="declaredVersion" />.
    /// </summary>
    /// <param name="modelName">Name of the model (without version).</param>
    /// <param name="declaredVersion">The version declared in <c>ckModel.yaml</c>.</param>
    /// <param name="catalogName">Restricts the lookup to this catalog; null queries every readable catalog.</param>
    /// <returns>The baseline decision.</returns>
    Task<CkBaselineResolution> ResolveAsync(string modelName, CkVersion declaredVersion, string? catalogName = null);

    /// <summary>
    ///     Resolves the baseline of <paramref name="modelName" /> for the <paramref name="declaredVersion" /> from the
    ///     given <paramref name="source" /> (AB#6294): <see cref="CkBaselineSource.Local" /> queries every readable
    ///     catalog (the local file-system catalog and the cached remote catalogs), <see cref="CkBaselineSource.Remote" />
    ///     ignores the local file-system catalog entirely. All other rules are those of the overload above.
    /// </summary>
    /// <param name="modelName">Name of the model (without version).</param>
    /// <param name="declaredVersion">The version declared in <c>ckModel.yaml</c>.</param>
    /// <param name="source">Which catalogs may supply the baseline.</param>
    /// <returns>The baseline decision.</returns>
    Task<CkBaselineResolution> ResolveAsync(string modelName, CkVersion declaredVersion, CkBaselineSource source);
}
