namespace Meshmakers.Octo.ConstructionKit.Contracts.Services;

/// <summary>
///     Options of the CK compiler.
/// </summary>
public class CkCompilerOptions
{
    /// <summary>
    ///     Name of the MSBuild property / environment variable that switches range retention on.
    /// </summary>
    public const string RangeRetentionPropertyName = "OctoCkRangeRetention";

    /// <summary>
    ///     CK v2 range retention (AB#5664, Phase 0 spike, default off). When true the compiler writes
    ///     <see cref="DataTransferObjects.CkCompiledModelRoot.DependencyRanges" /> (declared range + floor per
    ///     direct dependency) and stores references into dependencies major-qualified and model-version-less
    ///     (<c>System@2/Entity-1</c>) instead of pinning the highest catalog version
    ///     (<c>System-2.4.0/Entity-1</c>). When false the output is identical to a compiler without the
    ///     feature. Defaults to the environment variable <c>OctoCkRangeRetention</c> (<c>true</c>/<c>false</c>),
    ///     the same name as the MSBuild property, so an exported variable switches MSBuild and octo-ckc alike.
    /// </summary>
    public bool RangeRetention { get; set; } = string.Equals(
        Environment.GetEnvironmentVariable(RangeRetentionPropertyName), "true", StringComparison.OrdinalIgnoreCase);
}
