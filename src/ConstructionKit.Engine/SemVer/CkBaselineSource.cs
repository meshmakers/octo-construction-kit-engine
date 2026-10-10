namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     Which catalogs may supply the compatibility baseline of a model (AB#6294, MSBuild property
///     <c>OctoCkCompatibilityBaseline</c>).
/// </summary>
public enum CkBaselineSource
{
    /// <summary>
    ///     The local file-system catalog plus the cached remote catalogs. Needs no network. Default outside CI.
    /// </summary>
    Local,

    /// <summary>
    ///     The remote (GitHub) catalogs only; the local file-system catalog is ignored. Default in CI
    ///     (<c>TF_BUILD</c> / <c>ContinuousIntegrationBuild</c>).
    /// </summary>
    Remote
}
