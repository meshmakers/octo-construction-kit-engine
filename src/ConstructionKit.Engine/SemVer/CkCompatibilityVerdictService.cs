using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     Default implementation of <see cref="ICkCompatibilityVerdictService" />.
/// </summary>
public class CkCompatibilityVerdictService : ICkCompatibilityVerdictService
{
    private readonly ICatalogService _catalogService;
    private readonly ICkModelDiffService _diffService;
    private readonly ICkSemVerClassifier _classifier;

    /// <summary>
    ///     Creates a new instance of the <see cref="CkCompatibilityVerdictService" /> class.
    /// </summary>
    public CkCompatibilityVerdictService(ICatalogService catalogService, ICkModelDiffService diffService,
        ICkSemVerClassifier classifier)
    {
        _catalogService = catalogService;
        _diffService = diffService;
        _classifier = classifier;
    }

    /// <inheritdoc />
    public async Task<CkCompatibilityVerdict> EvaluateAsync(CkBaselineResolution resolution,
        CkCompiledModelRoot current, string? catalogName = null)
    {
        var publishedModelId = resolution.Baseline ??
                               throw new ArgumentException("The resolution has no baseline.", nameof(resolution));

        var baselineOperationResult = new OperationResult();
        var baselineCatalogName = catalogName ?? resolution.CatalogName;
        var baselineModel = baselineCatalogName != null
            ? await _catalogService.GetAsync(baselineCatalogName, publishedModelId, baselineOperationResult)
            : await _catalogService.GetAsync(publishedModelId, baselineOperationResult);
        if (baselineModel == null || baselineOperationResult.HasErrors || baselineOperationResult.HasFatalErrors)
        {
            throw new CompilerException(
                $"Error loading baseline model '{publishedModelId.FullName}' from catalog '{baselineCatalogName}'.",
                baselineOperationResult);
        }

        var declaredVersion = resolution.DeclaredVersion;
        var changes = _diffService.Diff(baselineModel, current);
        // AB#6585: the security exception for an access tightening is judged against the whole major line.
        var earlierInMajor = _classifier.NeedsMajorLineHistory(changes, baselineModel, current)
            ? await LoadEarlierInMajorAsync(resolution, publishedModelId, baselineCatalogName,
                    _classifier.NeedsFullModelHistory(changes, baselineModel, current)).ConfigureAwait(false)
            : [];
        var classifiedChanges = _classifier.Classify(changes, baselineModel, current, earlierInMajor);
        var requiredLevel = _classifier.GetRequiredLevel(classifiedChanges);
        var validation = _classifier.ValidateDeclaredVersion(publishedModelId.Version, declaredVersion,
            requiredLevel);
        var coveredLevel = GetCoveredLevel(publishedModelId.Version, declaredVersion);

        return new CkCompatibilityVerdict
        {
            Resolution = resolution,
            BaselineModel = baselineModel,
            ClassifiedChanges = classifiedChanges,
            RequiredLevel = requiredLevel,
            Validation = validation,
            CoveredLevel = coveredLevel,
            Acknowledgement = CkAcknowledgementResult.Evaluate(classifiedChanges, current.Compatibility,
                current.EffectiveCkLanguage >= 2 || baselineModel.EffectiveCkLanguage >= 2),
            UncoveredChanges = validation.Verdict == CkSemVerVerdict.VersionTooLow
                ? classifiedChanges.Where(c => c.Level > coveredLevel).ToList()
                : []
        };
    }

    /// <summary>
    ///     AB#6585: the published versions of the baseline's major below the baseline, read from the baseline's catalog
    ///     (the authoritative snapshot of a publish resolution when there is one). A version that cannot be loaded fails
    ///     the verdict: the history decides whether a security exception applies, so it is never guessed.
    ///     <paramref name="allMajors" /> (AB#7771, the first-ckLanguage-2-release exception is once per model) reads the
    ///     versions of every major below the baseline.
    /// </summary>
    private async Task<IReadOnlyList<CkCompiledModelRoot>> LoadEarlierInMajorAsync(CkBaselineResolution resolution,
        CkModelId baselineId, string? catalogName, bool allMajors)
    {
        IEnumerable<CkModelId> known;
        if (resolution.KnownVersions.Count > 0)
        {
            known = resolution.KnownVersions;
        }
        else if (catalogName != null)
        {
            var listing = await _catalogService.ListAsync(catalogName, 0, int.MaxValue).ConfigureAwait(false);
            known = (listing?.ModelResultItems ?? []).Select(i => i.ModelId);
        }
        else
        {
            known = [];
        }

        var earlier = known
            .Where(id => string.Equals(id.Name, baselineId.Name, StringComparison.OrdinalIgnoreCase) &&
                         (allMajors || id.Version.Major == baselineId.Version.Major) &&
                         id.Version.CompareTo(baselineId.Version) < 0)
            .GroupBy(id => id.Version)
            .Select(g => g.First())
            .OrderBy(id => id.Version)
            .ToList();
        var models = new List<CkCompiledModelRoot>();
        foreach (var id in earlier)
        {
            var result = new OperationResult();
            var model = catalogName != null
                ? await _catalogService.GetAsync(catalogName, id, result).ConfigureAwait(false)
                : await _catalogService.GetAsync(id, result).ConfigureAwait(false);
            if (model == null || result.HasErrors || result.HasFatalErrors)
            {
                throw new CompilerException(
                    $"Error loading '{id.FullName}' from catalog '{catalogName}' to judge the security exception " +
                    $"against the history of the model.",
                    result);
            }

            models.Add(model);
        }

        return models;
    }

    /// <summary>
    ///     The level of the bump from <paramref name="baseline" /> to <paramref name="declared" />; a declared version
    ///     below the baseline covers nothing.
    /// </summary>
    internal static CkSemVerLevel GetCoveredLevel(CkVersion baseline, CkVersion declared)
    {
        if (declared.CompareTo(baseline) <= 0)
        {
            return CkSemVerLevel.None;
        }

        if (declared.Major != baseline.Major)
        {
            return CkSemVerLevel.Major;
        }

        return declared.Minor != baseline.Minor ? CkSemVerLevel.Minor : CkSemVerLevel.Patch;
    }
}
