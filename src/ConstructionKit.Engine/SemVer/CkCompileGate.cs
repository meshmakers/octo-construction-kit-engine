using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     Severity of a <see cref="CkCompileGateMessage" />.
/// </summary>
public enum CkCompileGateSeverity
{
    /// <summary>Plain information for the build log (rendered with high importance).</summary>
    Info,

    /// <summary>A finding that fails the build.</summary>
    Error
}

/// <summary>
///     One line the compile gate wants in the build log.
/// </summary>
/// <param name="Severity">Info or error.</param>
/// <param name="Code">The <c>OCTO-CK2xx</c> code, null for plain information lines.</param>
/// <param name="Text">The message text without the code.</param>
public sealed record CkCompileGateMessage(CkCompileGateSeverity Severity, string? Code, string Text);

/// <summary>
///     Result of <see cref="CkCompileGate.RunAsync" />.
/// </summary>
public sealed record CkCompileGateResult
{
    /// <summary>The log lines in output order.</summary>
    public required IReadOnlyList<CkCompileGateMessage> Messages { get; init; }

    /// <summary>True when at least one message is an error: the build must fail and the model must not be published.</summary>
    public bool HasErrors => Messages.Any(m => m.Severity == CkCompileGateSeverity.Error);

    /// <summary>The verdict, null when there was no baseline to compare with or the gate did not run to the end.</summary>
    public CkCompatibilityVerdict? Verdict { get; init; }
}

/// <summary>
///     Gate 1 of the CK v2 compatibility concept (AB#6294, concept §4.3.3): <c>CkCompile</c> diffs a compiled model
///     against its baseline and fails the build for every change the declared version does not cover. Pure decision
///     logic over the shared <see cref="ICkBaselineResolver" /> and <see cref="ICkCompatibilityVerdictService" />;
///     it never writes to a catalog and has no MSBuild dependency, so it is unit-testable.
/// </summary>
/// <remarks>
///     ckLanguage 1 models only get the informational verdict line (never a warning, never an error, and any failure
///     of the check itself is swallowed into an information line); the CI step <c>validate-ck-versions</c> stays
///     their gate. ckLanguage 2 models are a hard gate.
/// </remarks>
public class CkCompileGate
{
    private readonly ICkBaselineResolver _baselineResolver;
    private readonly ICkCompatibilityVerdictService _verdictService;

    /// <summary>
    ///     Creates a new instance of the <see cref="CkCompileGate" /> class.
    /// </summary>
    public CkCompileGate(ICkBaselineResolver baselineResolver, ICkCompatibilityVerdictService verdictService)
    {
        _baselineResolver = baselineResolver;
        _verdictService = verdictService;
    }

    /// <summary>
    ///     Parses the value of the MSBuild property <c>OctoCkCompatibilityBaseline</c>.
    /// </summary>
    /// <param name="value">The property value; empty selects the default.</param>
    /// <param name="isContinuousIntegration">True on a CI build (<c>TF_BUILD</c> / <c>ContinuousIntegrationBuild</c>).</param>
    /// <param name="source">The effective baseline source.</param>
    /// <returns>False when the value is neither empty, <c>Local</c> nor <c>Remote</c>.</returns>
    public static bool TryGetBaselineSource(string? value, bool isContinuousIntegration, out CkBaselineSource source)
    {
        if (value == null || string.IsNullOrWhiteSpace(value))
        {
            source = isContinuousIntegration ? CkBaselineSource.Remote : CkBaselineSource.Local;
            return true;
        }

        if (string.Equals(value.Trim(), nameof(CkBaselineSource.Local), StringComparison.OrdinalIgnoreCase))
        {
            source = CkBaselineSource.Local;
            return true;
        }

        if (string.Equals(value.Trim(), nameof(CkBaselineSource.Remote), StringComparison.OrdinalIgnoreCase))
        {
            source = CkBaselineSource.Remote;
            return true;
        }

        source = CkBaselineSource.Local;
        return false;
    }

    /// <summary>
    ///     Runs the gate for a compiled model.
    /// </summary>
    /// <param name="current">The compiled model under test; its model id carries the declared version.</param>
    /// <param name="source">Which catalogs may supply the baseline.</param>
    /// <param name="modelPath">
    ///     The folder of the model's <c>ckModel.yaml</c>, named in the <c>OCTO-CK200</c> remediation
    ///     (<c>octo-ckc -c ValidateVersion -p … --apply</c>); a placeholder when null.
    /// </param>
    /// <returns>The messages for the build log and the verdict.</returns>
    public async Task<CkCompileGateResult> RunAsync(CkCompiledModelRoot current, CkBaselineSource source,
        string? modelPath = null)
    {
        var isHardGate = current.EffectiveCkLanguage >= 2;
        try
        {
            return await RunCoreAsync(current, source, isHardGate, modelPath);
        }
        catch (Exception ex) when (!isHardGate)
        {
            return new CkCompileGateResult
            {
                Messages =
                [
                    new CkCompileGateMessage(CkCompileGateSeverity.Info, null,
                        $"Compatibility check of '{current.ModelId}' skipped (ckLanguage 1, informational only): {ex.Message}")
                ]
            };
        }
    }

    private async Task<CkCompileGateResult> RunCoreAsync(CkCompiledModelRoot current, CkBaselineSource source,
        bool isHardGate, string? modelPath)
    {
        var modelName = current.ModelId.Name;
        var declared = current.ModelId.Version;
        var messages = new List<CkCompileGateMessage>();

        var resolution = await _baselineResolver.ResolveAsync(modelName, declared, source);

        if (resolution.Baseline == null)
        {
            if (resolution.SourceUnreachable)
            {
                var text = $"The compatibility baseline of model '{modelName}' could not be determined because a " +
                           $"catalog source was unreachable during the last cache refresh (baseline source: {source}). " +
                           (source == CkBaselineSource.Remote
                               ? "Check network connectivity and the catalog access tokens and retry. The gate is not skipped silently."
                               : "The model was compiled without a compatibility check; build with OctoRefreshRemoteCatalogs=true when online.");
                messages.Add(new CkCompileGateMessage(
                    isHardGate && source == CkBaselineSource.Remote ? CkCompileGateSeverity.Error : CkCompileGateSeverity.Info,
                    isHardGate ? "OCTO-CK202" : null, text));
            }
            else
            {
                messages.Add(new CkCompileGateMessage(CkCompileGateSeverity.Info, null,
                    $"Compatibility: model '{current.ModelId}', first publication (no baseline in the {source} baseline source)."));
                if (isHardGate)
                {
                    // Nothing to compare with, so no change can be acknowledged: every entry is stale (OCTO-CK204).
                    foreach (var (code, text) in CkAcknowledgementFormatter.GetFindings(
                                 CkAcknowledgementResult.Evaluate([], current.Compatibility, true), modelName))
                    {
                        messages.Add(new CkCompileGateMessage(CkCompileGateSeverity.Error, code, text));
                    }
                }
            }

            return new CkCompileGateResult { Messages = messages };
        }

        var verdict = await _verdictService.EvaluateAsync(resolution, current);
        messages.Add(new CkCompileGateMessage(CkCompileGateSeverity.Info, null, FormatVerdictLine(current, verdict)));
        if (resolution.SourceUnreachable)
        {
            messages.Add(new CkCompileGateMessage(CkCompileGateSeverity.Info, null,
                $"At least one catalog source was unreachable during the last cache refresh; baseline " +
                $"'{resolution.Baseline.FullName}' may be stale."));
        }

        foreach (var line in CkAcknowledgementFormatter.GetAcknowledgedLines(verdict.Acknowledgement))
        {
            messages.Add(new CkCompileGateMessage(CkCompileGateSeverity.Info, null, $"Acknowledged change: {line}"));
        }

        if (isHardGate)
        {
            AddErrors(messages, verdict, modelName, declared, modelPath);
            foreach (var (code, text) in CkAcknowledgementFormatter.GetFindings(verdict.Acknowledgement, modelName))
            {
                messages.Add(new CkCompileGateMessage(CkCompileGateSeverity.Error, code, text));
            }
        }

        return new CkCompileGateResult { Messages = messages, Verdict = verdict };
    }

    private static void AddErrors(List<CkCompileGateMessage> messages, CkCompatibilityVerdict verdict,
        string modelName, CkVersion declared, string? modelPath)
    {
        var validation = verdict.Validation;
        switch (validation.Verdict)
        {
            case CkSemVerVerdict.Downgrade:
                messages.Add(new CkCompileGateMessage(CkCompileGateSeverity.Error, "OCTO-CK201",
                    $"Declared version {declared} of model '{modelName}' is lower than the baseline version " +
                    $"{validation.PublishedVersion} of the same major. Downgrades are not allowed."));
                break;
            case CkSemVerVerdict.VersionTooLow:
                var minimum = $"{validation.MinimumVersion} (modelId: {modelName}-{validation.MinimumVersion}); " +
                              $"`octo-ckc -c ValidateVersion -p {modelPath ?? "<model folder>"} --apply` writes it";
                if (verdict.UncoveredChanges.Count == 0)
                {
                    messages.Add(new CkCompileGateMessage(CkCompileGateSeverity.Error, "OCTO-CK200",
                        $"Declared version {declared} of model '{modelName}' does not cover the changes since " +
                        $"{verdict.BaselineId.FullName}: a {CkModelChangeFormatter.GetLevelLabel(verdict.RequiredLevel)} " +
                        $"bump is required. Raise the version in ckModel.yaml to at least {minimum}."));
                    break;
                }

                foreach (var change in verdict.UncoveredChanges)
                {
                    messages.Add(new CkCompileGateMessage(CkCompileGateSeverity.Error, "OCTO-CK200",
                        $"{CkModelChangeFormatter.Format(change.Change)} requires a " +
                        $"{CkModelChangeFormatter.GetLevelLabel(change.Level)} bump over {verdict.BaselineId.FullName} " +
                        $"({change.Reason}), but declared version {declared} is only a " +
                        $"{CkModelChangeFormatter.GetLevelLabel(verdict.CoveredLevel)} bump. " +
                        $"Raise the version in ckModel.yaml to at least {minimum}."));
                }

                break;
        }
    }

    internal static string FormatVerdictLine(CkCompiledModelRoot current, CkCompatibilityVerdict verdict)
    {
        var resolution = verdict.Resolution;
        var catalog = (resolution.CatalogName ?? "unknown catalog") +
                      (resolution.IsLocal ? ", local, not published" : "");
        var required = verdict.RequiredLevel == CkSemVerLevel.None
            ? "none (no structural changes)"
            : CkModelChangeFormatter.GetLevelLabel(verdict.RequiredLevel);
        var outcome = verdict.Validation.IsValid ? "ok" : "version does not cover the changes";
        return $"Compatibility: model '{current.ModelId.Name}', declared {current.ModelId.Version}, " +
               $"baseline {resolution.Baseline!.FullName} ({catalog}), required level {required}, " +
               $"minimum version {verdict.Validation.MinimumVersion}: {outcome}.";
    }
}
