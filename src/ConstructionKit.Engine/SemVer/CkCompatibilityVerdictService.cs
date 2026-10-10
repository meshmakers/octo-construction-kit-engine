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
        var classifiedChanges = _classifier.Classify(changes, baselineModel, current);
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
            UncoveredChanges = validation.Verdict == CkSemVerVerdict.VersionTooLow
                ? classifiedChanges.Where(c => c.Level > coveredLevel).ToList()
                : []
        };
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
