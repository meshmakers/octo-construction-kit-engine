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
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            // First read: the cache of the catalog as is (it refreshes itself when older than its maximum age). Every
            // later read, and the confirming read right before the write, forces the refresh.
            await _catalogService.RefreshCatalogCacheAsync(catalog.CatalogName, sourceIdentifier,
                forceRefresh: evaluated != null).ConfigureAwait(false);

            var resolution = await _baselineResolver
                .ResolvePublishBaselineAsync(modelName, declared, catalog.CatalogName).ConfigureAwait(false);

            if (evaluated != null && evaluatedSnapshot != null && SameSnapshot(evaluatedSnapshot, resolution.KnownVersions))
            {
                return evaluated;
            }

            evaluated = await EvaluateResolvedAsync(catalog, model, resolution, allowsReplace).ConfigureAwait(false);
            evaluatedSnapshot = resolution.KnownVersions;
            // A refusal or a no-op never writes, so a stale read cannot do damage and needs no confirming read.
            if (evaluated.Decision is CkPublishGateDecision.AlreadyPublished or CkPublishGateDecision.Refused)
            {
                return evaluated;
            }
        }

        return Refused(Error("OCTO-CK202",
            $"Catalog '{catalog.CatalogName}' changed while the compatibility of model '{model.ModelId.FullName}' was " +
            $"checked ({MaxAttempts} reads). Nothing was written; retry the publish."));
    }

    private async Task<CkPublishGateResult> EvaluateResolvedAsync(ICatalog catalog, CkCompiledModelRoot model,
        CkBaselineResolution resolution, bool allowsReplace)
    {
        var modelName = model.ModelId.Name;
        var declared = model.ModelId.Version;
        var messages = new List<CkCompileGateMessage>();

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
                    Decision = CkPublishGateDecision.AlreadyPublished, Messages = messages
                };
            }

            if (!allowsReplace)
            {
                messages.Add(new CkCompileGateMessage(CkCompileGateSeverity.Error, "OCTO-CK205",
                    $"Model '{published.FullName}' is already published to catalog '{catalog.CatalogName}' with " +
                    "different content. Published versions are immutable (also with --replace); raise the version " +
                    "in ckModel.yaml and publish again."));
                return new CkPublishGateResult { Decision = CkPublishGateDecision.Refused, Messages = messages };
            }

            messages.Add(new CkCompileGateMessage(CkCompileGateSeverity.Info, null,
                $"Model '{published.FullName}' is replaced in lane catalog '{catalog.CatalogName}'."));
        }

        if (resolution.Baseline == null)
        {
            if (resolution.SourceUnreachable)
            {
                messages.Add(new CkCompileGateMessage(CkCompileGateSeverity.Error, "OCTO-CK202",
                    $"The compatibility baseline of model '{modelName}' could not be determined because catalog " +
                    $"'{catalog.CatalogName}' was unreachable during the last cache refresh. Check network " +
                    "connectivity and the catalog access tokens and retry. The gate is not skipped silently."));
                return new CkPublishGateResult { Decision = CkPublishGateDecision.Refused, Messages = messages };
            }

            messages.Add(new CkCompileGateMessage(CkCompileGateSeverity.Info, null,
                $"Compatibility: model '{model.ModelId}', first publication (no version in catalog '{catalog.CatalogName}')."));
            return new CkPublishGateResult { Decision = CkPublishGateDecision.Proceed, Messages = messages };
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
        var failed = messages.Any(m => m.Severity == CkCompileGateSeverity.Error);
        return new CkPublishGateResult
        {
            Decision = failed ? CkPublishGateDecision.Refused : CkPublishGateDecision.Proceed,
            Messages = messages,
            Verdict = verdict
        };
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
        new() { Decision = CkPublishGateDecision.Refused, Messages = [error] };
}
