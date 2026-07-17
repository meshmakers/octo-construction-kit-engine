using System.Text;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

/// <summary>
///     Validates that the version declared in <c>ckModel.yaml</c> honestly reflects the changes
///     made since the last published version of the model. The command never writes to the model
///     sources; with <c>--changelog</c> it additionally maintains a <c>CHANGELOG.md</c> next to
///     <c>ckModel.yaml</c>.
/// </summary>
internal class ValidateVersionCommand : CkcCommand
{
    private const string MetadataFileName = "ckModel.yaml";

    private readonly ICatalogService _catalogService;
    private readonly ICompilerService _compilerService;
    private readonly ICkSerializer _ckSerializer;
    private readonly ICkModelDiffService _diffService;
    private readonly ICkSemVerClassifier _classifier;
    private readonly ICkChangelogGenerator _changelogGenerator;
    private readonly IOptions<LocalFileSystemCatalogOptions> _localCatalogOptions;

    private readonly IArgument _pathArg;
    private readonly IArgument _catalogArg;
    private readonly IArgument _outputArg;
    private readonly IArgument _refreshArg;
    private readonly IArgument _changelogArg;
    private readonly IArgument _requireMigrationArg;
    private readonly IArgument _localCatalogEnabled;
    private readonly IArgument _localCatalogRoot;

    public ValidateVersionCommand(ILogger<ValidateVersionCommand> logger, IOptions<OctoToolOptions> options,
        ICatalogService catalogService, ICompilerService compilerService, ICkSerializer ckSerializer,
        ICkModelDiffService diffService, ICkSemVerClassifier classifier, ICkChangelogGenerator changelogGenerator,
        IOptions<LocalFileSystemCatalogOptions> localCatalogOptions)
        : base(logger, "ValidateVersion",
            "Validates that the declared construction kit model version reflects the changes since the last published version",
            options)
    {
        _catalogService = catalogService;
        _compilerService = compilerService;
        _ckSerializer = ckSerializer;
        _diffService = diffService;
        _classifier = classifier;
        _changelogGenerator = changelogGenerator;
        _localCatalogOptions = localCatalogOptions;

        _pathArg = CommandArgumentValue.AddArgument("p", "path",
            ["Root path(s) of construction kit model directories. Multiple paths are validated in the given order (dependency order)."],
            true, 1, true);

        _catalogArg = CommandArgumentValue.AddArgument("cn", "catalogName",
            ["Restricts baseline retrieval to the named catalog. By default, all readable catalogs are queried and the highest published version wins."],
            false, 1);

        _outputArg = CommandArgumentValue.AddArgument("o", "output",
            ["Additionally writes the validation report as Markdown to the given file (e.g. for PR comments)."],
            false, 1);

        _refreshArg = CommandArgumentValue.AddArgument("rf", "refresh",
            ["Forces a catalog cache refresh before the baseline is determined. Recommended for CI runs."], 0);

        _changelogArg = CommandArgumentValue.AddArgument("cl", "changelog",
            ["Writes/updates the section of the declared version in CHANGELOG.md next to ckModel.yaml. Only runs after successful validation."],
            0);

        _requireMigrationArg = CommandArgumentValue.AddArgument("rmm", "requireMigrationForMajor",
            ["Escalates a missing migration for a required major bump from a warning to an error."], 0);

        _localCatalogEnabled = CommandArgumentValue.AddArgument("lce", "localCatalogEnabled",
            ["Enable or disable the local Construction Kit Library catalog"], false, 1);

        _localCatalogRoot = CommandArgumentValue.AddArgument("lcr", "localCatalogRoot",
            ["Root path of the local Construction Kit Library catalog for this invocation only (not persisted)"],
            false, 1);
    }

    public override async Task Execute()
    {
        await base.Execute();

        Logger.LogInformation("Validating construction kit model version(s)");

        var rootPaths = CommandArgumentValue.GetArgumentValue(_pathArg).Values.ToList();
        var catalogName = CommandArgumentValue.IsArgumentUsed(_catalogArg)
            ? CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_catalogArg)
            : null;
        var outputFilePath = CommandArgumentValue.IsArgumentUsed(_outputArg)
            ? CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_outputArg)
            : null;
        var writeChangelog = CommandArgumentValue.IsArgumentUsed(_changelogArg);
        var requireMigrationForMajor = CommandArgumentValue.IsArgumentUsed(_requireMigrationArg);

        if (CommandArgumentValue.IsArgumentUsed(_localCatalogEnabled))
        {
            var isEnabled = CommandArgumentValue.GetArgumentScalarValueOrDefault<bool>(_localCatalogEnabled);
            Logger.LogInformation("Local Construction Kit catalog is {Status}", isEnabled ? "enabled" : "disabled");
            _localCatalogOptions.Value.IsEnabled = isEnabled;
        }

        if (CommandArgumentValue.IsArgumentUsed(_localCatalogRoot))
        {
            _localCatalogOptions.Value.ApplyRootPath(
                CommandArgumentValue.GetArgumentScalarValue<string>(_localCatalogRoot));
        }

        if (CommandArgumentValue.IsArgumentUsed(_refreshArg))
        {
            try
            {
                if (catalogName != null)
                {
                    Logger.LogInformation("Refreshing catalog cache of '{CatalogName}'", catalogName);
                    await _catalogService.RefreshCatalogCacheAsync(catalogName, forceRefresh: true);
                }
                else
                {
                    Logger.LogInformation("Refreshing all catalog caches");
                    await _catalogService.RefreshAllCatalogCachesAsync(forceRefresh: true);
                }
            }
            catch (ModelCatalogException ex)
            {
                // An unknown --catalogName (or other catalog-configuration problem) is a caller error,
                // not an internal fault: fail with a clean, actionable one-line message instead of the
                // raw stack trace the Runner's generic exception handler would emit. The Runner surfaces
                // ModelValidationException as a single-line error with a non-zero exit code.
                throw new ModelValidationException(
                    $"Catalog cache refresh failed: {ex.Message} Available catalogs: {DescribeAvailableCatalogs()}.", ex);
            }
        }

        var markdownReport = new StringBuilder("# Construction Kit SemVer Validation Report\n");
        var failures = new List<string>();

        foreach (var rootPath in rootPaths)
        {
            try
            {
                var packageErrors = await ValidatePackageAsync(rootPath, catalogName, writeChangelog,
                    requireMigrationForMajor, markdownReport);
                failures.AddRange(packageErrors);
            }
            catch (Exception ex) when (ex is CompilerException or ModelParseException or CkModelException
                                           or ModelCatalogException)
            {
                // Keep validating the remaining packages so that the report is comprehensive;
                // the collected failures fail the command at the end. These are expected
                // validation-type failures — emit a clean one-line error at default verbosity and
                // keep the full exception (stack trace) for --verbosity Detailed only.
                var message = $"'{rootPath}': {ex.Message}";
                Logger.LogError("Validation of construction kit at '{RootPath}' failed: {Message}", rootPath,
                    ex.Message);
                Logger.LogDebug(ex, "Validation of construction kit at '{RootPath}' failed", rootPath);
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
                $"Construction kit version validation failed for {failures.Count} finding(s):{Environment.NewLine} - " +
                string.Join($"{Environment.NewLine} - ", failures));
        }

        Logger.LogInformation("Construction kit model version(s) valid");
    }

    private async Task<List<string>> ValidatePackageAsync(string rootPath, string? catalogName, bool writeChangelog,
        bool requireMigrationForMajor, StringBuilder markdownReport)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var notes = new List<string>();

        // 1. Read ckModel.yaml — name, declared version, dependency ranges
        var metadataFilePath = Path.Combine(rootPath, MetadataFileName);
        if (!File.Exists(metadataFilePath))
        {
            throw new CompilerException($"Metadata file '{metadataFilePath}' does not exist.", new OperationResult());
        }

        var operationResult = new OperationResult();
        CkMetaRootDto meta;
        await using (var metadataStream = File.OpenRead(metadataFilePath))
        {
            meta = await _ckSerializer.DeserializeMetaAsync(metadataStream, metadataFilePath, operationResult);
        }

        if (operationResult.HasErrors || operationResult.HasFatalErrors)
        {
            throw new CompilerException($"Error reading metadata file '{metadataFilePath}'.", operationResult);
        }

        var modelName = meta.ModelId.Name;
        var declaredVersion = meta.ModelId.Version;
        Logger.LogInformation("Validating version of construction kit model '{ModelName}' at '{RootPath}'",
            modelName, rootPath);

        // 2. Determine the last published version (the baseline)
        var baselineRange = new CkModelIdVersionRange(modelName, "[0.0,)");
        var existingResult = catalogName != null
            ? await _catalogService.IsExistingAsync(catalogName, baselineRange)
            : await _catalogService.IsExistingAsync(baselineRange);

        if (!existingResult.Exists || existingResult.ModelId == null)
        {
            if (existingResult.SourceUnreachable)
            {
                errors.Add(
                    $"OCTO-CK102: The baseline for model '{modelName}' could not be determined because the catalog " +
                    "source was unreachable during the last cache refresh. Check network/VPN connectivity and the " +
                    "catalog configuration, then retry with --refresh. Validation is not skipped silently.");
                WriteReport(markdownReport, modelName, rootPath, null, declaredVersion, null, [], errors, warnings, notes);
                return errors.Select(e => $"{modelName}: {e}").ToList();
            }

            // First publication: any version is valid — the declared version is the starting point
            notes.Add("First publication — the model exists in no catalog yet; the declared version is the baseline.");
            WriteReport(markdownReport, modelName, rootPath, null, declaredVersion, null, [], errors, warnings, notes);

            if (writeChangelog)
            {
                await WriteChangelogAsync(rootPath, declaredVersion, CkSemVerLevel.None, [],
                    "Initial publication.");
            }

            return [];
        }

        var publishedModelId = existingResult.ModelId;
        if (existingResult.SourceUnreachable)
        {
            warnings.Add(
                "At least one catalog source was unreachable during the last cache refresh — the baseline " +
                $"'{publishedModelId.FullName}' may be stale. Retry with --refresh once the source is reachable.");
        }

        // 3. Dependency existence check (FR-9) — run BEFORE loading the baseline and compiling. An
        //    unsatisfiable dependency range otherwise aborts the in-memory compile with a raw
        //    ModelValidationException before OCTO-CK103 can be emitted. On failure we skip
        //    compile/diff for this package, write a clean report and continue with the next package.
        await CheckDependenciesAsync(meta, modelName, catalogName, errors);
        if (errors.Count > 0)
        {
            WriteReport(markdownReport, modelName, rootPath, existingResult, declaredVersion, null, [],
                errors, warnings, notes);
            return errors.Select(e => $"{modelName}: {e}").ToList();
        }

        // 4. Load the baseline model
        var baselineOperationResult = new OperationResult();
        var baselineCatalogName = catalogName ?? existingResult.CatalogName;
        var baseline = baselineCatalogName != null
            ? await _catalogService.GetAsync(baselineCatalogName, publishedModelId, baselineOperationResult)
            : await _catalogService.GetAsync(publishedModelId, baselineOperationResult);
        if (baseline == null || baselineOperationResult.HasErrors || baselineOperationResult.HasFatalErrors)
        {
            throw new CompilerException(
                $"Error loading baseline model '{publishedModelId.FullName}' from catalog '{baselineCatalogName}'.",
                baselineOperationResult);
        }

        // 5. Compile the current model in-memory (validation always runs against the compiled,
        //    canonically sorted model, never against raw source YAMLs)
        var compileOperationResult = new OperationResult();
        var current = await _compilerService.CompileInMemoryAsync(rootPath, compileOperationResult);

        // 6. Diff and classify
        var changes = _diffService.Diff(baseline, current);
        var classifiedChanges = _classifier.Classify(changes, baseline, current);
        var requiredLevel = _classifier.GetRequiredLevel(classifiedChanges);

        // 7. Apply the validation rule and reconcile migrations for major bumps
        var validationResult = _classifier.ValidateDeclaredVersion(publishedModelId.Version, declaredVersion,
            requiredLevel);
        switch (validationResult.Verdict)
        {
            case CkSemVerVerdict.VersionTooLow:
                errors.Add(
                    $"OCTO-CK100: Declared version {declaredVersion} of model '{modelName}' does not satisfy the " +
                    $"required {CkModelChangeFormatter.GetLevelLabel(requiredLevel)} bump over published version " +
                    $"{publishedModelId.Version}. Raise the version in ckModel.yaml to at least {validationResult.MinimumVersion} " +
                    $"(modelId: {modelName}-{validationResult.MinimumVersion}).");
                break;
            case CkSemVerVerdict.Downgrade:
                errors.Add(
                    $"OCTO-CK101: Declared version {declaredVersion} of model '{modelName}' is lower than the " +
                    $"published version {publishedModelId.Version}. Downgrades are not allowed.");
                break;
            case CkSemVerVerdict.ValidBumpWithoutStructuralChange:
                notes.Add("Version bump without structural model change (e.g. a semantic change) — legitimate.");
                break;
        }

        AddRenameHints(classifiedChanges, warnings);

        // Migration reconciliation (FR-10). Skip it while the declared version itself is still wrong
        // (too low, or a downgrade): OCTO-CK100/OCTO-CK101 already tell the developer to raise the
        // version, and reconciling here would name the *declared* version as the missing migration's
        // toVersion while OCTO-CK100 simultaneously demands a higher minimum — a contradictory hint.
        // Once the version is corrected the reconciliation re-runs against the right toVersion.
        if (requiredLevel == CkSemVerLevel.Major
            && validationResult.Verdict is not (CkSemVerVerdict.VersionTooLow or CkSemVerVerdict.Downgrade))
        {
            var hasMigrationForDeclaredVersion = HasMigrationForVersion(current, declaredVersion);
            if (!hasMigrationForDeclaredVersion)
            {
                var breakingChanges = classifiedChanges
                    .Where(c => c.Level == CkSemVerLevel.Major)
                    .Select(c => CkModelChangeFormatter.Format(c.Change))
                    .ToList();
                var breakingList = string.Join($"{Environment.NewLine}    ", breakingChanges);
                var migrationMessage =
                    $"The diff requires a major bump but no migration with toVersion {declaredVersion} exists. " +
                    $"Breaking changes:{Environment.NewLine}    {breakingList}";
                if (requireMigrationForMajor)
                {
                    errors.Add($"OCTO-CK104: {migrationMessage}");
                }
                else
                {
                    warnings.Add($"{migrationMessage}{Environment.NewLine}  A schema-only major without migrations " +
                                 "is allowed (no-migrations bridge); add a migration if runtime data must be transformed.");
                }
            }
        }

        // 9. Emit the report
        WriteReport(markdownReport, modelName, rootPath, existingResult, declaredVersion, validationResult,
            classifiedChanges, errors, warnings, notes);

        // 10. Only on success and --changelog: write/replace the section of the declared version
        if (errors.Count == 0 && writeChangelog)
        {
            var note = validationResult.Verdict == CkSemVerVerdict.ValidBumpWithoutStructuralChange
                ? "Version bump without structural model change."
                : null;
            await WriteChangelogAsync(rootPath, declaredVersion, requiredLevel, classifiedChanges, note);
        }

        return errors.Select(e => $"{modelName}: {e}").ToList();
    }

    /// <summary>
    ///     Verifies (never modifies) that every declared dependency range resolves to a published
    ///     version. Adds an OCTO-CK103 finding to <paramref name="errors" /> when a range is satisfied
    ///     by no published version, or OCTO-CK102 when the catalog source was unreachable during the
    ///     last cache refresh. Pinned to <paramref name="catalogName" /> when set, otherwise all
    ///     readable catalogs are queried.
    /// </summary>
    private async Task CheckDependenciesAsync(CkMetaRootDto meta, string modelName, string? catalogName,
        List<string> errors)
    {
        foreach (var dependencyRange in meta.Dependencies ?? [])
        {
            var dependencyResult = catalogName != null
                ? await _catalogService.IsExistingAsync(catalogName, dependencyRange)
                : await _catalogService.IsExistingAsync(dependencyRange);
            if (dependencyResult.Exists)
            {
                continue;
            }

            errors.Add(dependencyResult.SourceUnreachable
                ? $"OCTO-CK102: Dependency '{dependencyRange.FullName}' of model '{modelName}' could not be checked " +
                  "because the catalog source was unreachable. Check connectivity and retry with --refresh."
                : $"OCTO-CK103: Dependency range '{dependencyRange.FullName}' of model '{modelName}' is not satisfied " +
                  "by any published version. Publish the dependency first or correct the range in ckModel.yaml.");
        }
    }

    /// <summary>
    ///     Renders the readable catalogs as a comma-separated list for error messages.
    /// </summary>
    private string DescribeAvailableCatalogs()
    {
        var catalogs = _catalogService.GetCatalogList().Select(c => c.Item1).ToList();
        return catalogs.Count > 0 ? string.Join(", ", catalogs) : "<none>";
    }

    private static bool HasMigrationForVersion(CkCompiledModelRoot current, CkVersion declaredVersion)
    {
        var migrations = current.Migrations?.Meta.Migrations;
        if (migrations == null)
        {
            return false;
        }

        foreach (var migration in migrations)
        {
            try
            {
                if (new CkVersion(migration.ToVersion) == declaredVersion)
                {
                    return true;
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                // Malformed migration version — cannot match the declared version
            }
        }

        return false;
    }

    /// <summary>
    ///     A rename is not structurally detectable and appears as remove+add, which correctly
    ///     requires a major bump. When removals and additions of the same element kind occur
    ///     together, hint at this so the developer understands the classification.
    /// </summary>
    private static void AddRenameHints(IReadOnlyList<CkClassifiedModelChange> classifiedChanges, List<string> warnings)
    {
        var byKind = classifiedChanges
            .Where(c => c.Change.ChangeKind is CkModelChangeKind.Added or CkModelChangeKind.Removed)
            .GroupBy(c => c.Change.ElementKind);
        foreach (var kindGroup in byKind)
        {
            var removed = kindGroup.Where(c => c.Change.ChangeKind == CkModelChangeKind.Removed)
                .Select(c => c.Change.ElementId).ToList();
            var added = kindGroup.Where(c => c.Change.ChangeKind == CkModelChangeKind.Added)
                .Select(c => c.Change.ElementId).ToList();
            if (removed.Count > 0 && added.Count > 0)
            {
                warnings.Add(
                    $"Possibly a rename: {kindGroup.Key} removed ({string.Join(", ", removed)}) and added " +
                    $"({string.Join(", ", added)}) in the same diff. A rename is structurally remove+add and " +
                    "correctly requires a major bump.");
            }
        }
    }

    private async Task WriteChangelogAsync(string rootPath, CkVersion declaredVersion, CkSemVerLevel requiredLevel,
        IReadOnlyList<CkClassifiedModelChange> classifiedChanges, string? note)
    {
        var changelogFilePath = Path.Combine(rootPath, "CHANGELOG.md");
        var existingContent = File.Exists(changelogFilePath)
            ? await File.ReadAllTextAsync(changelogFilePath)
            : null;

        var updatedContent = _changelogGenerator.Generate(existingContent, declaredVersion, DateTime.UtcNow,
            requiredLevel, classifiedChanges, note);
        await File.WriteAllTextAsync(changelogFilePath, updatedContent);
        Logger.LogInformation("Changelog section for version {Version} written to '{ChangelogFilePath}'",
            declaredVersion, changelogFilePath);
    }

    private void WriteReport(StringBuilder markdownReport, string modelName, string rootPath,
        ModelExistingResult? baselineResult, CkVersion declaredVersion, CkSemVerValidationResult? validationResult,
        IReadOnlyList<CkClassifiedModelChange> classifiedChanges, List<string> errors, List<string> warnings,
        List<string> notes)
    {
        var isValid = errors.Count == 0;
        var resultLabel = isValid ? "VALID" : "ERROR";

        // Console report
        Console.WriteLine();
        Console.WriteLine($"SemVer validation: {modelName} ({rootPath})");
        if (baselineResult?.ModelId != null)
        {
            var cacheAge = baselineResult.CacheUpdatedAt == null
                ? "no cache timestamp"
                : $"cache updated {baselineResult.CacheUpdatedAt:u}, age {FormatAge(DateTime.UtcNow - baselineResult.CacheUpdatedAt.Value)}";
            Console.WriteLine($"  Published: {baselineResult.ModelId.Version} ({baselineResult.CatalogName ?? "unknown catalog"}, {cacheAge})");
        }
        else
        {
            Console.WriteLine("  Published: - (model not published yet)");
        }

        Console.WriteLine($"  Declared:  {declaredVersion}");
        if (validationResult != null)
        {
            Console.WriteLine(validationResult.RequiredLevel == CkSemVerLevel.None
                ? "  Required:  no bump required (no structural changes)"
                : $"  Required:  {CkModelChangeFormatter.GetLevelLabel(validationResult.RequiredLevel)} bump → minimum version {validationResult.MinimumVersion}");
        }

        if (classifiedChanges.Count > 0)
        {
            Console.WriteLine($"  Changes ({classifiedChanges.Count}):");
            foreach (var classifiedChange in classifiedChanges)
            {
                Console.WriteLine($"    {CkModelChangeFormatter.Format(classifiedChange)}");
            }
        }

        foreach (var note in notes)
        {
            Console.WriteLine($"  Note: {note}");
        }

        foreach (var warning in warnings)
        {
            Console.WriteLine($"  Warning: {warning}");
        }

        foreach (var error in errors)
        {
            Console.WriteLine($"  Error: {error}");
        }

        Console.WriteLine($"  Result: {resultLabel}");

        // Markdown report
        markdownReport.Append($"\n## {modelName} — {resultLabel}\n\n");
        if (baselineResult?.ModelId != null)
        {
            markdownReport.Append($"- Published: `{baselineResult.ModelId.Version}` ({baselineResult.CatalogName ?? "unknown catalog"})\n");
        }
        else
        {
            markdownReport.Append("- Published: — (model not published yet)\n");
        }

        markdownReport.Append($"- Declared: `{declaredVersion}`\n");
        if (validationResult != null)
        {
            markdownReport.Append(validationResult.RequiredLevel == CkSemVerLevel.None
                ? "- Required: no bump required (no structural changes)\n"
                : $"- Required: **{CkModelChangeFormatter.GetLevelLabel(validationResult.RequiredLevel)}** bump → minimum version `{validationResult.MinimumVersion}`\n");
        }

        if (classifiedChanges.Count > 0)
        {
            markdownReport.Append($"\n### Changes ({classifiedChanges.Count})\n\n");
            foreach (var classifiedChange in classifiedChanges)
            {
                markdownReport.Append(
                    $"- **{CkModelChangeFormatter.GetLevelLabel(classifiedChange.Level)}** {CkModelChangeFormatter.Format(classifiedChange.Change)} — {classifiedChange.Reason}\n");
            }
        }

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

    private static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age.TotalHours >= 1)
        {
            return $"{(int)age.TotalHours}h {age.Minutes:D2}m";
        }

        return age.TotalMinutes >= 1 ? $"{(int)age.TotalMinutes}m" : $"{(int)age.TotalSeconds}s";
    }
}
