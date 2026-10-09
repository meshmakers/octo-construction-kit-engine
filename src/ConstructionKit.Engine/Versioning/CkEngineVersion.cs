using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;

namespace Meshmakers.Octo.ConstructionKit.Engine.Versioning;

/// <summary>
///     CK v2 (F1.1-S6, AB#5909): the <c>minEngineVersion</c> of compiled models and the check against the running
///     engine.
/// </summary>
public static class CkEngineVersion
{
    /// <summary>
    ///     The lowest engine version that reads <c>ckLanguage: 2</c> and range-retaining models. Deterministic (not
    ///     the compiling engine's own version) so compile output does not depend on the build configuration. Engines
    ///     before CK v2 ignore the key and are kept away from such models by the <c>ck-models/v3/</c> catalog path;
    ///     raise this value when a later engine adds model features that this engine line cannot read.
    /// </summary>
    public const string CkV2MinEngineVersion = "3.4.0";

    private static readonly Version? RunningVersion = ParseVersion(typeof(CkEngineVersion).Assembly.GetName().Version);

    /// <summary>
    ///     The version of the running engine (major.minor.patch), or <c>null</c> for a development build whose
    ///     assembly version is below 1.0 (e.g. private-feed builds <c>0.1.*</c>); those skip the check. DebugL builds
    ///     are <c>999.0.0</c> and therefore accept every model.
    /// </summary>
    public static Version? Current => RunningVersion;

    /// <summary>
    ///     The <c>minEngineVersion</c> the compiler writes for a model: <see cref="CkV2MinEngineVersion" /> for a
    ///     <c>ckLanguage: 2</c> or range-retaining model, otherwise <c>null</c> (v1 output unchanged).
    /// </summary>
    public static string? GetRequiredMinEngineVersion(CkCompiledModelRoot model) =>
        model.EffectiveCkLanguage >= 2 || model.IsRangeRetaining ? CkV2MinEngineVersion : null;

    /// <summary>
    ///     The highest of several <c>minEngineVersion</c> values (<c>null</c> entries ignored; <c>null</c> when none).
    /// </summary>
    public static string? Max(IEnumerable<string?> minEngineVersions) =>
        minEngineVersions.Where(v => v != null && Version.TryParse(v, out _))
            .OrderByDescending(v => Version.Parse(v!))
            .FirstOrDefault();

    /// <summary>
    ///     True when an engine of version <paramref name="engineVersion" /> can read a model with the given
    ///     <c>minEngineVersion</c>. An unparsable value is treated as not satisfied (fail closed).
    /// </summary>
    public static bool IsSatisfiedBy(string? minEngineVersion, Version? engineVersion)
    {
        if (minEngineVersion == null || engineVersion == null)
        {
            return true;
        }

        return Version.TryParse(minEngineVersion, out var required) &&
               Normalize(engineVersion).CompareTo(Normalize(required)) >= 0;
    }

    /// <summary>
    ///     Message 126 when the running engine is older than the model's <c>minEngineVersion</c>.
    /// </summary>
    /// <returns>True when the model can be read.</returns>
    public static bool CheckModel(CkModelRootBase model, string? location, OperationResult operationResult,
        Version? engineVersion = null)
    {
        if (model is not CkCompiledModelRoot { MinEngineVersion: { } minEngineVersion })
        {
            return true;
        }

        var running = engineVersion ?? Current;
        if (IsSatisfiedBy(minEngineVersion, running))
        {
            return true;
        }

        operationResult.AddMessage(MessageCodes.CkModelRequiresNewerEngine(location, model.ModelId, minEngineVersion,
            running?.ToString(3) ?? "unknown"));
        return false;
    }

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));

    private static Version? ParseVersion(Version? assemblyVersion) =>
        assemblyVersion == null || assemblyVersion.Major < 1 ? null : Normalize(assemblyVersion);
}
