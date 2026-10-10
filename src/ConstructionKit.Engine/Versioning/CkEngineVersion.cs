using System.Reflection;
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
    ///     The lowest engine version that reads <c>ckLanguage: 2</c> and range-retaining models: 3.5.1, the first
    ///     libs release with the full CK v2 Phase 1 reader (AB#6390). 3.5.0 and the 3.4.x line cannot read such
    ///     models and refuse them with message 126. Deterministic (not the compiling engine's own version) so compile
    ///     output does not depend on the build configuration. Models already published with an older value keep it
    ///     (published models are immutable). Raise this value when a later engine adds model features that this
    ///     engine line cannot read.
    /// </summary>
    public const string CkV2MinEngineVersion = "3.5.1";

    private static readonly Version? RunningVersion = ReadRunningVersion(typeof(CkEngineVersion).Assembly);

    // Test seam (AB#6274): never set outside tests, so production always reads RunningVersion.
    private static readonly AsyncLocal<Version?> CurrentOverride = new();

    /// <summary>
    ///     The version of the running engine (major.minor.patch), or <c>null</c> for a main-line build whose version
    ///     is below 1.0 (private-feed builds <c>0.1.YYMM.NNNN</c>); those skip the check (see
    ///     <see cref="IsSatisfiedBy" />). DebugL builds are <c>999.0.0</c> and therefore accept every model. It is
    ///     read patch-exact from the assembly file version, see <see cref="ReadRunningVersion" />.
    /// </summary>
    public static Version? Current => CurrentOverride.Value ?? RunningVersion;

    /// <summary>
    ///     Test seam (AB#6274): makes <see cref="Current" /> return <paramref name="engineVersion" /> for the current
    ///     async flow until the returned scope is disposed, so tests of message 126 behave the same under DebugL
    ///     (<c>999.0.0</c>), private-feed CI builds (<c>0.1.*</c>, where <see cref="Current" /> is otherwise
    ///     <c>null</c>) and release builds (<c>3.x</c>). Production code never calls it.
    /// </summary>
    internal static IDisposable OverrideCurrentForTests(Version engineVersion)
    {
        var previous = CurrentOverride.Value;
        CurrentOverride.Value = Normalize(engineVersion);
        return new RestoreScope(() => CurrentOverride.Value = previous);
    }

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
    ///     <b>Defined rule for the main line (AB#6390):</b> an unknown engine version (<c>null</c>) and an engine
    ///     below 1.0 (main-line private-feed builds <c>0.1.YYMM.NNNN</c>) skip the check: that line carries no number
    ///     comparable with the release line (3.5.1); it is covered by the main-line floor of the engine inventory
    ///     and by rebuilt images.
    /// </summary>
    public static bool IsSatisfiedBy(string? minEngineVersion, Version? engineVersion)
    {
        if (minEngineVersion == null || engineVersion == null || engineVersion.Major < 1)
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

    private sealed class RestoreScope(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));

    /// <summary>
    ///     Reads the engine version patch-exact (AB#6390). The pipeline stamps the AssemblyVersion as
    ///     <c>Major.Minor.0.0</c> (<c>set-version.yml</c>), so it cannot tell 3.5.0 from 3.5.2; the
    ///     <c>AssemblyFileVersion</c> carries the full <c>Major.Minor.Patch.Revision</c> (<c>$(BuildNumberLong)</c>,
    ///     e.g. <c>3.5.2.0</c> on an <c>r3.5.2</c> build, <c>0.1.2610.9010</c> on main). It is preferred over the
    ///     <c>AssemblyInformationalVersion</c> because it is always purely numeric (no <c>-branch</c> slug of test
    ///     builds, no <c>+sourcelink</c> hash). Order: file version, informational version (suffixes stripped),
    ///     assembly version. A missing or unparsable value falls through to the next; the engine never throws.
    /// </summary>
    internal static Version? ReadRunningVersion(Assembly assembly)
    {
        var version = TryParseEngineVersion(assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version)
                      ?? TryParseEngineVersion(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                          ?.InformationalVersion)
                      ?? TryParseEngineVersion(assembly.GetName().Version?.ToString());
        return version == null || version.Major < 1 ? null : version;
    }

    /// <summary>
    ///     Parses <c>3.5.2</c>, <c>3.5.2.0</c>, <c>3.5.2-rc1</c> and <c>3.5.2.0+sha</c> to <c>3.5.2</c>; <c>null</c>
    ///     when the text is not a version with at least major.minor.
    /// </summary>
    internal static Version? TryParseEngineVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var end = text!.IndexOfAny(new[] { '+', '-' });
        var numeric = end >= 0 ? text.Substring(0, end) : text;
        return Version.TryParse(numeric.Trim(), out var version) ? Normalize(version) : null;
    }
}
