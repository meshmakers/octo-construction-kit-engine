namespace Meshmakers.Octo.ConstructionKit.Contracts.Services;

/// <summary>
///     Options of the CK compiler.
/// </summary>
public class CkCompilerOptions
{
    /// <summary>
    ///     Name of the obsolete MSBuild property / environment variable that used to switch range retention on
    ///     (AB#6675). It has no effect any more; a build that still sets it gets the deprecation warning
    ///     <see cref="RangeRetentionObsoleteCode" />.
    /// </summary>
    public const string RangeRetentionPropertyName = "OctoCkRangeRetention";

    /// <summary>Message number of the deprecation warning for the obsolete property, option and environment variable.</summary>
    public const string RangeRetentionObsoleteCode = "OCTO-CK300";

    /// <summary>Text of the deprecation warning (message <see cref="RangeRetentionObsoleteCode" />).</summary>
    public const string RangeRetentionObsoleteText =
        "OctoCkRangeRetention / -rr is obsolete and ignored: range retention is the compiler default (AB#6675). " +
        "Remove the property, the option and the environment variable.";

    /// <summary>
    ///     CK v2 range retention (AB#5664, the compiler default since AB#6675). The compiler writes
    ///     <see cref="DataTransferObjects.CkCompiledModelRoot.DependencyRanges" /> (declared range + floor per
    ///     direct dependency) and stores references into dependencies major-qualified and model-version-less
    ///     (<c>System@2/Entity-1</c>) instead of pinning the highest catalog version
    ///     (<c>System-2.4.0/Entity-1</c>). There is no user-facing switch (MSBuild property, option and environment
    ///     variable are gone); this property stays only as a code seam for tests and tooling that must produce the
    ///     classic exact-pinned form of a model (compatibility tests of the published v1 models, the one-time re-pin).
    /// </summary>
    public bool RangeRetention { get; set; } = true;
}
