using System.Text;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.BlueprintManager.Commands.Implementations;

/// <summary>
///     Validates that the version declared in <c>blueprint.yaml</c> honestly reflects the changes
///     made since the last published version of the blueprint. The command never writes anywhere —
///     neither to the blueprint sources nor to a catalog.
/// </summary>
/// <remarks>
///     Rules, per blueprint path (validated against the named catalog, or all readable catalogs):
///     <list type="bullet">
///         <item>The declared version is already published and the content is identical — valid; a
///             release publish will skip it (no-op).</item>
///         <item>The declared version is already published but the content differs — OCTO-BP100:
///             the version must be raised; published versions are immutable.</item>
///         <item>The declared version is not published and lower than the highest published
///             version — OCTO-BP101: downgrades are not allowed.</item>
///         <item>The declared version is new and higher — valid; identical content to the highest
///             published version only earns a warning (version bump without content change).</item>
///         <item>A declared blueprint dependency satisfied by no published version and no sibling
///             blueprint validated earlier in the same invocation — OCTO-BP103.</item>
///     </list>
///     Content comparison covers every file below the blueprint directory (publish uploads the
///     whole directory), with line endings normalized. A file that exists only in the published
///     version (deleted locally) is not detectable through the catalog read API — renaming or
///     removing a file therefore requires a version bump by review discipline.
///     CK model dependencies (<c>ckModelDependencies</c>) are resolved against the CK model
///     catalogs, which this tool does not configure — they are validated by the blueprint engine
///     at install time and by octo-ckc during CK builds, not here.
/// </remarks>
internal class ValidateVersionCommand : CatalogReadCommand
{
    private readonly IBlueprintCompilerService _blueprintCompilerService;
    private readonly IEnumerable<IBlueprintCatalog> _catalogs;
    private readonly IArgument _pathArg;
    private readonly IArgument _catalogArg;
    private readonly IArgument _outputArg;

    private IReadOnlyList<BlueprintCatalogRefreshResult> _refreshResults = [];

    public ValidateVersionCommand(
        ILogger<ValidateVersionCommand> logger,
        IOptions<BpmToolOptions> options,
        IBlueprintCatalogManager catalogManager,
        IBlueprintCompilerService blueprintCompilerService,
        IEnumerable<IBlueprintCatalog> catalogs)
        : base(logger, "validateVersion",
            "Validates that the declared blueprint version reflects the changes since the last published version",
            options, catalogManager)
    {
        _blueprintCompilerService = blueprintCompilerService;
        _catalogs = catalogs;

        _pathArg = CommandArgumentValue.AddArgument("p", "path",
            ["Path(s) of blueprint directories. Multiple paths are validated in the given order (dependency order)."],
            true, 1, true);

        _catalogArg = CommandArgumentValue.AddArgument("cn", "catalogName",
            [
                "Restricts baseline retrieval and the dependency-existence check to the named catalog.",
                "By default, all readable catalogs are queried and the highest published version wins."
            ],
            false, 1);

        _outputArg = CommandArgumentValue.AddArgument("o", "output",
            ["Additionally writes the validation report as Markdown to the given file (e.g. for build summaries)."],
            false, 1);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Forces the cache refresh like every catalog-read command, but keeps the per-catalog
    ///     results: a failed refresh of a catalog this validation reads from must fail the
    ///     validation (OCTO-BP102) — otherwise a published blueprint would silently validate as a
    ///     first publication whenever the catalog source is unreachable.
    /// </remarks>
    public override async Task PreValidate()
    {
        _refreshResults = await CatalogManager.RefreshAllCatalogCachesAsync(force: true);
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Validating blueprint version(s)");

        var rootPaths = CommandArgumentValue.GetArgumentValue(_pathArg).Values.ToList();
        var catalogName = CommandArgumentValue.IsArgumentUsed(_catalogArg)
            ? CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_catalogArg)
            : null;
        var outputFilePath = CommandArgumentValue.IsArgumentUsed(_outputArg)
            ? CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_outputArg)
            : null;

        var targetCatalogs = ResolveTargetCatalogs(catalogName);

        var markdownReport = new StringBuilder("# Blueprint Version Validation Report\n");
        var failures = new List<string>();

        // Blueprints validated earlier in THIS invocation, keyed by name. Paths are passed in
        // dependency order, so a later blueprint may depend on a version that is introduced by an
        // earlier blueprint of the same commit and is therefore not published yet.
        var validatedSiblings = new Dictionary<string, BlueprintId>(StringComparer.Ordinal);

        foreach (var rootPath in rootPaths)
        {
            try
            {
                var errors = await ValidateBlueprintAsync(rootPath, targetCatalogs, markdownReport,
                    validatedSiblings);
                failures.AddRange(errors);
            }
            catch (BlueprintCatalogException ex)
            {
                // Keep validating the remaining blueprints so that the report is comprehensive;
                // the collected failures fail the command at the end.
                var message = $"'{rootPath}': {ex.Message}";
                Logger.LogError("Validation of blueprint at '{RootPath}' failed: {Message}", rootPath, ex.Message);
                Logger.LogDebug(ex, "Validation of blueprint at '{RootPath}' failed", rootPath);
                markdownReport.Append($"\n## {rootPath} — ERROR\n\n{ex.Message}\n");
                failures.Add(message);
            }
        }

        if (outputFilePath != null)
        {
            var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputFilePath));
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            await File.WriteAllTextAsync(outputFilePath, markdownReport.ToString());
            Logger.LogInformation("Markdown report written to '{OutputFilePath}'", outputFilePath);
        }

        if (failures.Count > 0)
        {
            throw new ModelValidationException(
                $"Blueprint version validation failed for {failures.Count} finding(s):{Environment.NewLine} - " +
                string.Join($"{Environment.NewLine} - ", failures));
        }

        Logger.LogInformation("Blueprint version(s) valid");
    }

    private List<IBlueprintCatalog> ResolveTargetCatalogs(string? catalogName)
    {
        var readableCatalogs = _catalogs.Where(c => c.CanRead).OrderBy(c => c.Order).ToList();

        if (catalogName == null)
        {
            return readableCatalogs;
        }

        var namedCatalog = readableCatalogs.FirstOrDefault(c =>
            string.Equals(c.CatalogName, catalogName, StringComparison.OrdinalIgnoreCase));
        if (namedCatalog == null)
        {
            var availableCatalogs = string.Join(", ", readableCatalogs.Select(c => c.CatalogName));
            throw new ModelValidationException(
                $"Catalog '{catalogName}' is not a readable blueprint catalog. Available catalogs: {availableCatalogs}.");
        }

        return [namedCatalog];
    }

    private async Task<List<string>> ValidateBlueprintAsync(string rootPath,
        IReadOnlyList<IBlueprintCatalog> targetCatalogs, StringBuilder markdownReport,
        Dictionary<string, BlueprintId> validatedSiblings)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var notes = new List<string>();

        // 1. Validate the blueprint sources (schema + structure) and read the declared id
        var operationResult = new OperationResult();
        BlueprintMetaRootDto meta;
        try
        {
            meta = await _blueprintCompilerService.ValidateAsync(rootPath, operationResult);
        }
        catch (BlueprintCatalogException)
        {
            operationResult.WriteMessagesToLogger(Logger);
            throw;
        }

        var declaredId = meta.BlueprintId;
        var blueprintName = declaredId.Name;
        Logger.LogInformation("Validating version of blueprint '{BlueprintName}' at '{RootPath}'",
            blueprintName, rootPath);

        // 2. A failed cache refresh of a target catalog invalidates every baseline decision
        //     (OCTO-BP102): without the catalog we cannot distinguish "first publication" from
        //     "source unreachable".
        foreach (var catalog in targetCatalogs)
        {
            var refreshResult = _refreshResults.FirstOrDefault(r =>
                string.Equals(r.CatalogName, catalog.CatalogName, StringComparison.OrdinalIgnoreCase));
            if (refreshResult is { Status: BlueprintCatalogRefreshStatus.Failed })
            {
                errors.Add(
                    $"OCTO-BP102: The baseline for blueprint '{blueprintName}' could not be determined because " +
                    $"catalog '{catalog.CatalogName}' failed to refresh ({refreshResult.Message}). Check " +
                    "connectivity and the catalog configuration. Validation is not skipped silently.");
            }
        }

        if (errors.Count > 0)
        {
            WriteReport(markdownReport, rootPath, declaredId, null, null, errors, warnings, notes);
            return errors.Select(e => $"{blueprintName}: {e}").ToList();
        }

        // 3. Determine the published baseline: the highest published version across the target
        //     catalogs, and — independently — whether the DECLARED version itself is published.
        var baselineRange = new BlueprintIdVersionRange(blueprintName, "[0.0,)");
        BlueprintId? highestPublished = null;
        IBlueprintCatalog? highestCatalog = null;
        IBlueprintCatalog? declaredCatalog = null;
        foreach (var catalog in targetCatalogs)
        {
            var existingResult = await catalog.IsExistingAsync(baselineRange);
            if (existingResult is { Exists: true, BlueprintId: not null })
            {
                if (highestPublished == null || existingResult.BlueprintId.Version.CompareTo(highestPublished.Version) > 0)
                {
                    highestPublished = existingResult.BlueprintId;
                    highestCatalog = catalog;
                }
            }

            if (declaredCatalog == null && await catalog.IsExistingAsync(declaredId))
            {
                declaredCatalog = catalog;
            }
        }

        // 4. Apply the validation rules
        if (highestPublished == null)
        {
            notes.Add("First publication — the blueprint exists in no catalog yet; the declared version is the baseline.");
        }
        else if (declaredCatalog != null)
        {
            var driftingFiles = await CompareContentAsync(declaredCatalog, declaredId, rootPath);
            if (driftingFiles.Count > 0)
            {
                errors.Add(
                    $"OCTO-BP100: The content of blueprint '{blueprintName}' differs from the published version " +
                    $"{declaredId.Version} but the declared version was not raised. Published versions are " +
                    $"immutable — raise the version in blueprint.yaml above {highestPublished.Version}. " +
                    $"Changed: {string.Join(", ", driftingFiles)}.");
            }
            else if (declaredId.Version.CompareTo(highestPublished.Version) < 0)
            {
                notes.Add(
                    $"Declared version {declaredId.Version} is published and unchanged; the highest published " +
                    $"version is {highestPublished.Version}.");
            }
            else
            {
                notes.Add(
                    $"Declared version {declaredId.Version} is already published with identical content — a " +
                    "release publish will skip it.");
            }
        }
        else if (declaredId.Version.CompareTo(highestPublished.Version) < 0)
        {
            errors.Add(
                $"OCTO-BP101: Declared version {declaredId.Version} of blueprint '{blueprintName}' is lower than " +
                $"the highest published version {highestPublished.Version}. Downgrades are not allowed.");
        }
        else
        {
            var driftingFiles = await CompareContentAsync(highestCatalog!, highestPublished, rootPath);
            if (driftingFiles.Count == 0)
            {
                warnings.Add(
                    $"Version bump without content change: the declared version {declaredId.Version} is " +
                    $"byte-identical to the published version {highestPublished.Version}.");
            }
            else
            {
                notes.Add(
                    $"New version {declaredId.Version} over published {highestPublished.Version} " +
                    $"({driftingFiles.Count} changed file(s)) — published on the next release.");
            }
        }

        // 5. Blueprint dependency existence check, honouring siblings validated earlier in this run
        foreach (var dependencyRange in meta.BlueprintDependencies ?? [])
        {
            var dependencyExists = false;
            foreach (var catalog in targetCatalogs)
            {
                var dependencyResult = await catalog.IsExistingAsync(dependencyRange);
                if (dependencyResult.Exists)
                {
                    dependencyExists = true;
                    break;
                }
            }

            if (dependencyExists)
            {
                continue;
            }

            if (validatedSiblings.TryGetValue(dependencyRange.Name, out var siblingId)
                && dependencyRange.IsSatisfiedBy(siblingId))
            {
                notes.Add(
                    $"Dependency '{dependencyRange.FullName}' is not published yet but satisfied by sibling " +
                    $"blueprint '{siblingId.FullName}' validated earlier in this run — it is published during " +
                    "the same release.");
                continue;
            }

            errors.Add(
                $"OCTO-BP103: Dependency range '{dependencyRange.FullName}' of blueprint '{blueprintName}' is " +
                "not satisfied by any published version. Publish the dependency first or correct the range in " +
                "blueprint.yaml.");
        }

        // 6. Register for sibling dependency resolution of later blueprints in this run
        if (errors.Count == 0)
        {
            validatedSiblings[blueprintName] = declaredId;
        }

        WriteReport(markdownReport, rootPath, declaredId, highestPublished, highestCatalog?.CatalogName,
            errors, warnings, notes);

        return errors.Select(e => $"{blueprintName}: {e}").ToList();
    }

    /// <summary>
    ///     Compares every file below the local blueprint directory against the published version.
    ///     Returns the relative paths (forward slashes) whose content differs or which are missing
    ///     from the published version. The comparison is byte-based (binary-safe); CRLF sequences
    ///     are normalized to LF first so a checkout with different autocrlf settings does not
    ///     report false drift.
    /// </summary>
    private static async Task<List<string>> CompareContentAsync(IBlueprintCatalog catalog,
        BlueprintId publishedId, string rootPath)
    {
        var driftingFiles = new List<string>();
        var fullRootPath = Path.GetFullPath(rootPath);

        foreach (var filePath in Directory.GetFiles(fullRootPath, "*", SearchOption.AllDirectories).Order())
        {
            var relativePath = Path.GetRelativePath(fullRootPath, filePath).Replace('\\', '/');
            var localContent = NormalizeLineEndings(await File.ReadAllBytesAsync(filePath));

            byte[]? publishedContent;
            try
            {
                await using var publishedStream = await catalog.OpenBlueprintFileAsync(publishedId, relativePath);
                using var publishedBuffer = new MemoryStream();
                await publishedStream.CopyToAsync(publishedBuffer);
                publishedContent = NormalizeLineEndings(publishedBuffer.ToArray());
            }
            catch (BlueprintFileNotFoundException)
            {
                publishedContent = null;
            }

            if (publishedContent == null)
            {
                driftingFiles.Add($"{relativePath} (not in published version)");
            }
            else if (!localContent.AsSpan().SequenceEqual(publishedContent))
            {
                driftingFiles.Add(relativePath);
            }
        }

        return driftingFiles;
    }

    /// <summary>
    ///     Removes the CR of every CRLF sequence. Operates on raw bytes so non-text files are
    ///     compared safely; a lone CR is left untouched (it is content, not a line ending shared
    ///     between checkout styles).
    /// </summary>
    private static byte[] NormalizeLineEndings(byte[] content)
    {
        var normalized = new byte[content.Length];
        var length = 0;
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == (byte)'\r' && i + 1 < content.Length && content[i + 1] == (byte)'\n')
            {
                continue;
            }

            normalized[length++] = content[i];
        }

        return normalized[..length];
    }

    private void WriteReport(StringBuilder markdownReport, string rootPath, BlueprintId declaredId,
        BlueprintId? highestPublished, string? publishedCatalogName, List<string> errors, List<string> warnings,
        List<string> notes)
    {
        var isValid = errors.Count == 0;
        var resultLabel = isValid ? "VALID" : "ERROR";

        // Console report
        Console.WriteLine();
        Console.WriteLine($"Blueprint version validation: {declaredId.Name} ({rootPath})");
        Console.WriteLine(highestPublished != null
            ? $"  Published: {highestPublished.Version} ({publishedCatalogName ?? "unknown catalog"})"
            : "  Published: - (blueprint not published yet)");
        Console.WriteLine($"  Declared:  {declaredId.Version}");

        foreach (var note in notes)
        {
            Console.WriteLine($"  Note: {note}");
        }

        foreach (var warning in warnings)
        {
            Console.WriteLine($"  Warning: {warning}");
            Logger.LogWarning("{Warning}", warning);
        }

        foreach (var error in errors)
        {
            Console.WriteLine($"  Error: {error}");
        }

        Console.WriteLine($"  Result: {resultLabel}");

        // Markdown report
        markdownReport.Append($"\n## {declaredId.Name} — {resultLabel}\n\n");
        markdownReport.Append(highestPublished != null
            ? $"- Published: `{highestPublished.Version}` ({publishedCatalogName ?? "unknown catalog"})\n"
            : "- Published: — (blueprint not published yet)\n");
        markdownReport.Append($"- Declared: `{declaredId.Version}`\n");

        AppendMarkdownList(markdownReport, "Notes", notes);
        AppendMarkdownList(markdownReport, "Warnings", warnings);
        AppendMarkdownList(markdownReport, "Errors", errors);
    }

    private static void AppendMarkdownList(StringBuilder markdownReport, string heading, List<string> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        markdownReport.Append($"\n### {heading}\n\n");
        foreach (var entry in entries)
        {
            markdownReport.Append($"- {entry.Replace(Environment.NewLine, " ")}\n");
        }
    }
}
