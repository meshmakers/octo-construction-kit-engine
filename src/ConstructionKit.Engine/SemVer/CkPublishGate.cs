using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     What the publish gate decided.
/// </summary>
public enum CkPublishGateDecision
{
    /// <summary>The gate does not apply (local file-system catalog, catalog not writable or unknown).</summary>
    NotGated,

    /// <summary>The model may be written.</summary>
    Proceed,

    /// <summary>The identical content is already published under this version: nothing to write.</summary>
    AlreadyPublished,

    /// <summary>The publish is refused; nothing must be written.</summary>
    Refused
}

/// <summary>
///     Result of <see cref="CkPublishGate.EvaluateAsync" />.
/// </summary>
public sealed record CkPublishGateResult
{
    /// <summary>The decision.</summary>
    public required CkPublishGateDecision Decision { get; init; }

    /// <summary>The log lines in output order; errors carry the <c>OCTO-CK2xx</c> code.</summary>
    public IReadOnlyList<CkCompileGateMessage> Messages { get; init; } = [];

    /// <summary>The verdict, null when there was no baseline to compare with or the gate stopped before.</summary>
    public CkCompatibilityVerdict? Verdict { get; init; }

    /// <summary>
    ///     The state of the version indexes the verdict was computed against (AB#6584); the write is conditional on it.
    ///     Null for a catalog that cannot tell (it is then read through its cache).
    /// </summary>
    public CatalogStateToken? State { get; init; }

    /// <summary>True when at least one message is an error.</summary>
    public bool HasErrors => Messages.Any(m => m.Severity == CkCompileGateSeverity.Error);
}

/// <summary>
///     Gate 2 of the CK v2 compatibility concept (AB#6386, concept §4.3.3): every publish to a writable catalog other
///     than the local file-system catalog gets the same compatibility verdict as the compile gate
///     (<see cref="CkCompileGate" />, codes <c>OCTO-CK200</c> to <c>OCTO-CK202</c>), computed against the catalog being
///     published to. It lives in <c>CatalogService.PublishAsync</c>, so <c>octo-ckc publish</c>, the MSBuild publish of
///     <c>CkCompile</c> and any other caller are covered by one implementation and no pipeline can forget it.
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item>Baseline: <see cref="ICkBaselineResolver.ResolvePublishBaselineAsync" /> (target catalog only, never the
///             declared version itself, case-insensitive model names, both catalog roots).</item>
///         <item>Same-version rule: identical content already published = <see cref="CkPublishGateDecision.AlreadyPublished" />;
///             equivalent content (same after canonicalisation, <see cref="CkPublishEquivalence" />: restated default
///             ownership, element order, compiler-stamped metadata) = <see cref="CkPublishGateDecision.AlreadyPublished" />
///             with a warning naming the difference class; different content = <c>OCTO-CK205</c> unless the catalog allows replacing (<see cref="IReplaceablePublishCatalog" />,
///             the dev lane catalog). There is no bypass switch; <c>--replace</c> does not skip the gate.</item>
///         <item>Race: the catalog is re-read right before the write; if it changed since the baseline was read, the verdict
///             is computed again (at most <see cref="MaxAttempts" /> reads), never written on a stale baseline.</item>
///         <item>The verdict is computed from the model content and the target catalog only; nothing in the compiled
///             file lowers the required level. ckLanguage 1 and 2 are treated alike (platform owner decision 2026-10-10).</item>
///     </list>
/// </remarks>
public class CkPublishGate
{
    /// <summary>How often the target catalog is read before a concurrently changing catalog fails the publish.</summary>
    public const int MaxAttempts = 3;

    private readonly ICkBaselineResolver _baselineResolver;
    private readonly ICkCompatibilityVerdictService _verdictService;
    private readonly ICatalogService _catalogService;
    private readonly IEnumerable<ICatalog> _catalogs;
    private readonly ICkJsonSerializer _jsonSerializer;

    /// <summary>
    ///     Creates a new instance of the <see cref="CkPublishGate" /> class.
    /// </summary>
    public CkPublishGate(ICkBaselineResolver baselineResolver, ICkCompatibilityVerdictService verdictService,
        ICatalogService catalogService, IEnumerable<ICatalog> catalogs, ICkJsonSerializer jsonSerializer)
    {
        _baselineResolver = baselineResolver;
        _verdictService = verdictService;
        _catalogService = catalogService;
        _catalogs = catalogs;
        _jsonSerializer = jsonSerializer;
    }

    /// <summary>
    ///     Evaluates the publish of <paramref name="model" /> to <paramref name="catalogName" />. Reads only (it
    ///     refreshes the catalog cache); never writes to a catalog.
    /// </summary>
    /// <param name="catalogName">The catalog the model is published to.</param>
    /// <param name="model">The compiled model; the verdict is computed from its content.</param>
    /// <param name="sourceIdentifier">The source identifier of the publish.</param>
    /// <returns>The decision and the messages for the log.</returns>
    public async Task<CkPublishGateResult> EvaluateAsync(string catalogName, CkCompiledModelRoot model,
        object? sourceIdentifier = null)
    {
        var catalog = _catalogs.FirstOrDefault(c =>
            string.Equals(c.CatalogName, catalogName, StringComparison.OrdinalIgnoreCase));
        // Unknown, read-only, foreign-source and local catalogs: the catalog manager reports the first three itself; the
        // local file-system catalog holds local builds and is never a publish target of record.
        if (catalog == null || !catalog.CanWrite || !catalog.IsSupportingSourceIdentifier(sourceIdentifier) ||
            CkBaselineResolver.IsLocalCatalog(catalog.CatalogName))
        {
            return new CkPublishGateResult { Decision = CkPublishGateDecision.NotGated };
        }

        var modelName = model.ModelId.Name;
        var declared = model.ModelId.Version;
        var allowsReplace = catalog is IReplaceablePublishCatalog { AllowsReplacingPublishedVersions: true };

        if (!catalog.CanRead)
        {
            return Refused(Error("OCTO-CK202",
                $"The compatibility baseline of model '{modelName}' could not be determined because catalog " +
                $"'{catalog.CatalogName}' is not readable (disabled). The gate is not skipped silently."));
        }

        CkPublishGateResult? evaluated = null;
        IReadOnlyList<CkModelId>? evaluatedSnapshot = null;
        try
        {
            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                // A catalog with an authoritative source (the GitHub catalogs, AB#6580) is read from its repository by the
                // baseline resolver on every attempt, so the cache - filled from a lagging mirror - plays no role. Every
                // other catalog: first read the cache as is (it refreshes itself when older than its maximum age); every
                // later read, and the confirming read right before the write, forces the refresh.
                if (catalog is not IAuthoritativeVersionCatalog)
                {
                    await _catalogService.RefreshCatalogCacheAsync(catalog.CatalogName, sourceIdentifier,
                        forceRefresh: evaluated != null).ConfigureAwait(false);
                }

                var resolution = await _baselineResolver
                    .ResolvePublishBaselineAsync(modelName, declared, catalog.CatalogName).ConfigureAwait(false);

                if (evaluated != null && evaluatedSnapshot != null &&
                    SameSnapshot(evaluatedSnapshot, resolution.KnownVersions))
                {
                    return evaluated with { State = resolution.State };
                }

                evaluated = await EvaluateResolvedAsync(catalog, model, resolution, allowsReplace).ConfigureAwait(false);
                evaluatedSnapshot = resolution.KnownVersions;
                // A refusal or a no-op never writes, so a stale read cannot do damage and needs no confirming read.
                if (evaluated.Decision is CkPublishGateDecision.AlreadyPublished or CkPublishGateDecision.Refused)
                {
                    return evaluated;
                }
            }
        }
        catch (Exception e) when (IsSourceFailure(e))
        {
            // Fail closed (AB#6580): a read of the target catalog that fails anywhere in the gate - offline, rate limit,
            // timeout, a model file the index lists but the repository lacks - is OCTO-CK202, never an unhandled error
            // and never a verdict taken from what could not be read.
            return Refused(Error("OCTO-CK202", UnreachableText(modelName, catalog.CatalogName, DescribeFailure(e))));
        }

        return Refused(Error("OCTO-CK202",
            $"Catalog '{catalog.CatalogName}' changed while the compatibility of model '{model.ModelId.FullName}' was " +
            $"checked ({MaxAttempts} reads). Nothing was written; retry the publish."));
    }

    /// <summary>
    ///     The check after the write (AB#6584), the second half of the race protection: the catalog is read again and
    ///     the published version is judged against its predecessor as the catalog holds it now. It finds what the
    ///     conditional write cannot guard: a concurrent publish that landed in an index the write does not compare (a
    ///     case-variant spelling of the model name, another major). Returns null when the version stands.
    /// </summary>
    /// <param name="catalogName">The catalog the model was published to.</param>
    /// <param name="model">The model that was just written.</param>
    /// <param name="judgedBaseline">The baseline the verdict before the write used; null for a first publication.</param>
    /// <param name="sourceIdentifier">The source identifier of the publish.</param>
    /// <returns>The refusal to report (the version must be withdrawn), or null.</returns>
    public async Task<CkPublishGateResult?> VerifyAfterWriteAsync(string catalogName, CkCompiledModelRoot model,
        CkModelId? judgedBaseline, object? sourceIdentifier = null)
    {
        var catalog = _catalogs.FirstOrDefault(c =>
            string.Equals(c.CatalogName, catalogName, StringComparison.OrdinalIgnoreCase));
        if (catalog == null || !catalog.CanWrite || !catalog.CanRead || !catalog.IsSupportingSourceIdentifier(sourceIdentifier) ||
            CkBaselineResolver.IsLocalCatalog(catalog.CatalogName))
        {
            return null;
        }

        var modelName = model.ModelId.Name;
        try
        {
            if (catalog is not IAuthoritativeVersionCatalog)
            {
                await _catalogService.RefreshCatalogCacheAsync(catalog.CatalogName, sourceIdentifier, forceRefresh: true)
                    .ConfigureAwait(false);
            }

            // The version is published now, so the baseline is the newest version below it in its major (or the highest
            // lower major): its predecessor as the catalog holds it after every concurrent publish.
            var resolution = await _baselineResolver
                .ResolvePublishBaselineAsync(modelName, model.ModelId.Version, catalog.CatalogName).ConfigureAwait(false);
            if (resolution.SourceUnreachable)
            {
                return Refused(Error("OCTO-CK202", UnreachableText(modelName, catalog.CatalogName,
                                                       resolution.SourceUnreachableReason) +
                                                   $" Model '{model.ModelId.FullName}' was written but could not be verified against a concurrent publish; publishing it again completes the check."));
            }

            // Nothing changed below the version since the verdict: it stands.
            if (resolution.Baseline == null || (judgedBaseline != null && resolution.Baseline.Equals(judgedBaseline)))
            {
                return null;
            }

            var verdict = await _verdictService.EvaluateAsync(resolution, model, catalog.CatalogName)
                .ConfigureAwait(false);
            var messages = new List<CkCompileGateMessage>();
            CkCompileGate.AddErrors(messages, verdict, modelName, model.ModelId.Version, null);
            AddAcknowledgementMessages(messages, model, verdict.Acknowledgement);
            if (!messages.Any(m => m.Severity == CkCompileGateSeverity.Error))
            {
                return null;
            }

            messages.Add(Error(CkPublishRefusal.ConcurrentPublishCode,
                $"Model '{model.ModelId.FullName}' was written, but another publish of '{modelName}' landed at the same " +
                $"time: judged against its predecessor '{resolution.Baseline.FullName}' as the catalog holds it now, the " +
                "version violates the rules above."));
            return new CkPublishGateResult
            {
                Decision = CkPublishGateDecision.Refused, Messages = CkPublishRefusal.Normalize(messages),
                Verdict = verdict
            };
        }
        catch (Exception e) when (IsSourceFailure(e))
        {
            return Refused(Error("OCTO-CK202", UnreachableText(modelName, catalog.CatalogName, DescribeFailure(e)) +
                                               $" Model '{model.ModelId.FullName}' was written but could not be verified against a concurrent publish; publishing it again completes the check."));
        }
    }

    /// <summary>The failures of reading the target catalog that the gate turns into <c>OCTO-CK202</c>.</summary>
    private static bool IsSourceFailure(Exception e) =>
        e is HttpRequestException or Octokit.ApiException or ModelCatalogException or OperationCanceledException
            or System.Text.Json.JsonException or IOException;

    private static string DescribeFailure(Exception e) => e switch
    {
        OperationCanceledException => "the request timed out",
        Octokit.ApiException api => $"GitHub answered HTTP {(int)api.StatusCode}: {api.ApiError?.Message ?? api.Message}",
        _ => e.Message
    };

    private static string UnreachableText(string modelName, string catalogName, string? reason) =>
        $"The compatibility baseline of model '{modelName}' could not be determined because catalog '{catalogName}' " +
        $"could not be read{(string.IsNullOrWhiteSpace(reason) ? "" : $": {reason}")}. Nothing was written. Check network " +
        "connectivity, the catalog repository and the access token and retry. The gate is not skipped silently.";

    private async Task<CkPublishGateResult> EvaluateResolvedAsync(ICatalog catalog, CkCompiledModelRoot model,
        CkBaselineResolution resolution, bool allowsReplace)
    {
        var modelName = model.ModelId.Name;
        var declared = model.ModelId.Version;
        var messages = new List<CkCompileGateMessage>();

        // The authoritative read failed (AB#6580): nothing below can be judged, and the catalog must not be asked again
        // for the burned numbers - that would only fail the same way.
        if (resolution is { Baseline: null, SourceUnreachable: true, SourceUnreachableReason: not null })
        {
            return Refused(Error("OCTO-CK202",
                UnreachableText(modelName, catalog.CatalogName, resolution.SourceUnreachableReason)));
        }

        // Burned number (AB#5433): reported together with the other reasons; the write checks it again.
        if (catalog is IWithdrawnVersionCatalog withdrawnCatalog)
        {
            var withdrawn = await withdrawnCatalog.FindWithdrawnVersionAsync(model.ModelId).ConfigureAwait(false);
            if (withdrawn != null)
            {
                var because = string.IsNullOrWhiteSpace(withdrawn.Reason) ? "" : $" ({withdrawn.Reason})";
                messages.Add(new CkCompileGateMessage(CkCompileGateSeverity.Error, CkPublishRefusal.BurnedVersionCode,
                    $"Version '{model.ModelId.FullName}' was unpublished from catalog '{catalog.CatalogName}' on " +
                    $"{withdrawn.WithdrawnAt:yyyy-MM-dd}{because}. A withdrawn version number is burned and is never " +
                    "published again."));
            }
        }

        // Same-version rule: published versions are immutable.
        if (resolution.DeclaredVersionPublished != null)
        {
            var published = resolution.DeclaredVersionPublished;
            var equivalence = await CompareWithPublishedAsync(catalog, published, model).ConfigureAwait(false);
            if (equivalence.AreEquivalent)
            {
                if (equivalence.DifferenceClasses.Count == 0)
                {
                    messages.Add(new CkCompileGateMessage(CkCompileGateSeverity.Info, null,
                        $"Model '{published.FullName}' is already published to catalog '{catalog.CatalogName}' with " +
                        "identical content; nothing to publish."));
                }
                else
                {
                    // Equivalent content: only compiler-generated or restated-default differences. The published file
                    // stays as it is; the next real change brings the new form with its own version.
                    messages.Add(new CkCompileGateMessage(CkCompileGateSeverity.Warning, null,
                        $"Model '{published.FullName}' is already published to catalog '{catalog.CatalogName}' with " +
                        "equivalent content; nothing to publish. The compiled file differs only in: " +
                        string.Join("; ", equivalence.DifferenceClasses) +
                        ". The published version stays as it is."));
                }

                return new CkPublishGateResult
                {
                    Decision = CkPublishGateDecision.AlreadyPublished, Messages = CkPublishRefusal.Normalize(messages)
                };
            }

            if (!allowsReplace)
            {
                // No early return (AB#6389): the publisher raises the version next, so the reasons that apply to the
                // raised version (compatibility, acknowledgements) are reported now, not one refusal at a time.
                messages.Add(new CkCompileGateMessage(CkCompileGateSeverity.Error, "OCTO-CK205",
                    $"Model '{published.FullName}' is already published to catalog '{catalog.CatalogName}' with " +
                    "different content. Published versions are immutable (also with --replace); raise the version " +
                    "in ckModel.yaml and publish again."));
            }
            else
            {
                messages.Add(new CkCompileGateMessage(CkCompileGateSeverity.Info, null,
                    $"Model '{published.FullName}' is replaced in lane catalog '{catalog.CatalogName}'."));
            }
        }

        if (resolution.Baseline == null)
        {
            if (resolution.SourceUnreachable)
            {
                messages.Add(new CkCompileGateMessage(CkCompileGateSeverity.Error, "OCTO-CK202",
                    $"The compatibility baseline of model '{modelName}' could not be determined because catalog " +
                    $"'{catalog.CatalogName}' was unreachable during the last cache refresh. Check network " +
                    "connectivity and the catalog access tokens and retry. The gate is not skipped silently."));
                return new CkPublishGateResult
                {
                    Decision = CkPublishGateDecision.Refused, Messages = CkPublishRefusal.Normalize(messages)
                };
            }

            messages.Add(new CkCompileGateMessage(CkCompileGateSeverity.Info, null,
                $"Compatibility: model '{model.ModelId}', first publication (no version in catalog '{catalog.CatalogName}')."));
            if (model.EffectiveCkLanguage >= 2)
            {
                // Nothing to compare with, so no change can be acknowledged: every entry is stale (OCTO-CK204).
                AddAcknowledgementMessages(messages, model, CkAcknowledgementResult.Evaluate([], model.Compatibility, true));
            }

            return new CkPublishGateResult
            {
                Decision = messages.Any(m => m.Severity == CkCompileGateSeverity.Error)
                    ? CkPublishGateDecision.Refused
                    : CkPublishGateDecision.Proceed,
                Messages = CkPublishRefusal.Normalize(messages)
            };
        }

        var verdict = await _verdictService.EvaluateAsync(resolution, model, catalog.CatalogName)
            .ConfigureAwait(false);
        messages.Add(new CkCompileGateMessage(CkCompileGateSeverity.Info, null,
            CkCompileGate.FormatVerdictLine(model, verdict)));
        if (resolution.SourceUnreachable)
        {
            messages.Add(new CkCompileGateMessage(CkCompileGateSeverity.Info, null,
                $"Catalog '{catalog.CatalogName}' was unreachable during the last cache refresh; baseline " +
                $"'{resolution.Baseline.FullName}' may be stale."));
        }

        CkCompileGate.AddErrors(messages, verdict, modelName, declared, null);
        // Acknowledgements travel in the compiled model, so the publisher needs no source (AB#6387). An acknowledgement
        // never lowers a level: OCTO-CK200 above is independent of it. Same rule code as the compile gate.
        AddAcknowledgementMessages(messages, model, verdict.Acknowledgement);
        var failed = messages.Any(m => m.Severity == CkCompileGateSeverity.Error);
        return new CkPublishGateResult
        {
            Decision = failed ? CkPublishGateDecision.Refused : CkPublishGateDecision.Proceed,
            Messages = CkPublishRefusal.Normalize(messages),
            Verdict = verdict
        };
    }

    /// <summary>
    ///     The "Acknowledged changes" lines (audit trail of the publish) and the findings: entries of the compiled model
    ///     that violate the structural rules (<c>OCTO-CK206</c>), changes that need an acknowledgement and have none
    ///     (<c>OCTO-CK203</c>) and entries that match no change of this release (<c>OCTO-CK204</c>). Nothing is added
    ///     when the rule is not enforced (ckLanguage 1 on both sides).
    /// </summary>
    private static void AddAcknowledgementMessages(List<CkCompileGateMessage> messages, CkCompiledModelRoot model,
        CkAcknowledgementResult acknowledgement)
    {
        if (!acknowledgement.IsEnforced)
        {
            return;
        }

        var lines = CkAcknowledgementFormatter.GetAcknowledgedLines(acknowledgement);
        if (lines.Count > 0)
        {
            messages.Add(new CkCompileGateMessage(CkCompileGateSeverity.Info, null,
                $"Acknowledged changes of model '{model.ModelId.FullName}' (level, change, reason):"));
            foreach (var line in lines)
            {
                messages.Add(new CkCompileGateMessage(CkCompileGateSeverity.Info, null, $"  {line}"));
            }
        }

        var findings = CkAcknowledgementFormatter
            .GetInvalidEntryFindings(model.Compatibility?.Acknowledge, model.ModelId.Name)
            .Concat(CkAcknowledgementFormatter.GetFindings(acknowledgement, model.ModelId.Name));
        foreach (var (code, text) in findings)
        {
            messages.Add(new CkCompileGateMessage(CkCompileGateSeverity.Error, code, text));
        }
    }

    private async Task<CkPublishEquivalenceResult> CompareWithPublishedAsync(ICatalog catalog, CkModelId published,
        CkCompiledModelRoot model)
    {
        var operationResult = new OperationResult();
        var existing = await catalog.GetAsync(published, operationResult).ConfigureAwait(false);
        return await new CkPublishEquivalence(_jsonSerializer).CompareAsync(existing, model).ConfigureAwait(false);
    }

    private static bool SameSnapshot(IReadOnlyList<CkModelId> a, IReadOnlyList<CkModelId> b) =>
        a.Count == b.Count && a.Zip(b, (x, y) => x.Equals(y)).All(equal => equal);

    private static CkCompileGateMessage Error(string code, string text) =>
        new(CkCompileGateSeverity.Error, code, text);

    private static CkPublishGateResult Refused(CkCompileGateMessage error) =>
        new() { Decision = CkPublishGateDecision.Refused, Messages = CkPublishRefusal.Normalize([error]) };
}
