using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

/// <summary>
///     Whole-catalog consistency check (AB#4469): loads every model of a catalog at its latest version and reports which
///     ones no longer resolve (missing pinned dependency, used surface removed or changed incompatibly) and which model
///     names differ only in case. Read-only and standalone: never part of a plain build. A non-zero exit code means a
///     defect in a Meshmakers-run catalog (the private or the public GitHub catalog); in any other catalog defects are
///     warnings.
/// </summary>
internal class ValidateCatalogCommand : CkcCommand
{
    private readonly ICatalogService _catalogService;
    private readonly ICkCatalogValidationService _validationService;
    private readonly IOptions<LocalFileSystemCatalogOptions> _localCatalogOptions;

    private readonly IArgument _catalogArg;
    private readonly IArgument _modelArg;
    private readonly IArgument _outputArg;
    private readonly IArgument _refreshArg;
    private readonly IArgument _localCatalogEnabled;
    private readonly IArgument _localCatalogRoot;

    public ValidateCatalogCommand(ILogger<ValidateCatalogCommand> logger, IOptions<OctoToolOptions> options,
        ICatalogService catalogService, ICkCatalogValidationService validationService,
        IOptions<LocalFileSystemCatalogOptions> localCatalogOptions)
        : base(logger, "ValidateCatalog",
            "Checks that every model of a catalog still resolves at its latest version (read-only, standalone)",
            options)
    {
        _catalogService = catalogService;
        _validationService = validationService;
        _localCatalogOptions = localCatalogOptions;

        _catalogArg = CommandArgumentValue.AddArgument("cn", "catalogName",
            ["Name of the catalog to validate, e.g. PublicGitHubCatalog, PrivateGitHubCatalog or LocalFileSystemCatalog."],
            true, 1);

        _modelArg = CommandArgumentValue.AddArgument("m", "model",
            ["Restricts the check to the named model(s) and their dependents. By default the whole catalog is checked."],
            false, 1, true);

        _outputArg = CommandArgumentValue.AddArgument("o", "output",
            ["Additionally writes the report as Markdown to the given file."], false, 1);

        _refreshArg = CommandArgumentValue.AddArgument("rf", "refresh",
            ["Forces a catalog cache refresh before the catalog is read. Recommended for CI runs."], 0);

        _localCatalogEnabled = CommandArgumentValue.AddArgument("lce", "localCatalogEnabled",
            ["Enable or disable the local Construction Kit Library catalog"], false, 1);

        _localCatalogRoot = CommandArgumentValue.AddArgument("lcr", "localCatalogRoot",
            ["Root path of the local Construction Kit Library catalog for this invocation only (not persisted)"],
            false, 1);
    }

    public override async Task Execute()
    {
        await base.Execute();

        var catalogName = CommandArgumentValue.GetArgumentScalarValue<string>(_catalogArg);
        var models = CommandArgumentValue.IsArgumentUsed(_modelArg)
            ? CommandArgumentValue.GetArgumentValue(_modelArg).Values.ToList()
            : [];
        var outputFilePath = CommandArgumentValue.IsArgumentUsed(_outputArg)
            ? CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_outputArg)
            : null;

        if (CommandArgumentValue.IsArgumentUsed(_localCatalogEnabled))
        {
            _localCatalogOptions.Value.IsEnabled =
                CommandArgumentValue.GetArgumentScalarValueOrDefault<bool>(_localCatalogEnabled);
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
                await _catalogService.RefreshCatalogCacheAsync(catalogName, forceRefresh: true);
            }
            catch (ModelCatalogException ex)
            {
                throw new ModelValidationException($"Catalog cache refresh failed: {ex.Message}", ex);
            }
        }

        Logger.LogInformation("Validating catalog {CatalogName}", catalogName);
        var result = await _validationService.ValidateAsync(catalogName, models);

        Console.WriteLine();
        Console.Write(CkCatalogValidationReport.RenderConsole(result));

        if (outputFilePath != null)
        {
            var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputFilePath));
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            await File.WriteAllTextAsync(outputFilePath, CkCatalogValidationReport.RenderMarkdown(result));
            Logger.LogInformation("Markdown report written to '{OutputFilePath}'", outputFilePath);
        }

        if (result.HasErrors)
        {
            throw new ModelValidationException(
                $"Catalog '{catalogName}' is not consistent: {result.BrokenCount} model(s) do not resolve at their latest " +
                $"version, {result.CaseVariants.Count} model name(s) exist in case-only variants.");
        }
    }
}
