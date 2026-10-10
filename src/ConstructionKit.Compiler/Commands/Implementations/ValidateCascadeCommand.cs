using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

/// <summary>
///     Dry run of a base model's next version against every model that depends on it (AB#5437): for each dependent,
///     transitive ones included, it prints whether the dependent stays <c>Compatible</c>, <c>NeedsRepin</c> or
///     <c>Breaks</c> and why. Read-only: the candidate is compiled in memory and registered nowhere, no catalog is
///     written. A non-zero exit code means at least one dependent breaks.
/// </summary>
internal class ValidateCascadeCommand : CkcCommand
{
    private readonly ICatalogService _catalogService;
    private readonly ICompilerService _compilerService;
    private readonly ICkCascadeService _cascadeService;
    private readonly IOptions<LocalFileSystemCatalogOptions> _localCatalogOptions;

    private readonly IArgument _pathArg;
    private readonly IArgument _catalogArg;
    private readonly IArgument _outputArg;
    private readonly IArgument _refreshArg;
    private readonly IArgument _localCatalogEnabled;
    private readonly IArgument _localCatalogRoot;

    public ValidateCascadeCommand(ILogger<ValidateCascadeCommand> logger, IOptions<OctoToolOptions> options,
        ICatalogService catalogService, ICompilerService compilerService, ICkCascadeService cascadeService,
        IOptions<LocalFileSystemCatalogOptions> localCatalogOptions)
        : base(logger, "ValidateCascade",
            "Dry run: checks the next version of a base model against every model that depends on it, without publishing or writing anything",
            options)
    {
        _catalogService = catalogService;
        _compilerService = compilerService;
        _cascadeService = cascadeService;
        _localCatalogOptions = localCatalogOptions;

        _pathArg = CommandArgumentValue.AddArgument("p", "path",
            ["Root path of the candidate construction kit model (the base model whose next version is checked)."],
            true, 1);

        _catalogArg = CommandArgumentValue.AddArgument("cn", "catalogName",
            ["Restricts the baseline and the discovery of dependents to the named catalog. By default all readable catalogs are queried."],
            false, 1);

        _outputArg = CommandArgumentValue.AddArgument("o", "output",
            ["Additionally writes the report as Markdown to the given file."], false, 1);

        _refreshArg = CommandArgumentValue.AddArgument("rf", "refresh",
            ["Forces a catalog cache refresh before the dependents are discovered."], 0);

        _localCatalogEnabled = CommandArgumentValue.AddArgument("lce", "localCatalogEnabled",
            ["Enable or disable the local Construction Kit Library catalog"], false, 1);

        _localCatalogRoot = CommandArgumentValue.AddArgument("lcr", "localCatalogRoot",
            ["Root path of the local Construction Kit Library catalog for this invocation only (not persisted)"],
            false, 1);
    }

    public override async Task Execute()
    {
        await base.Execute();

        var rootPath = CommandArgumentValue.GetArgumentScalarValue<string>(_pathArg);
        var catalogName = CommandArgumentValue.IsArgumentUsed(_catalogArg)
            ? CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_catalogArg)
            : null;
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
                if (catalogName != null)
                {
                    await _catalogService.RefreshCatalogCacheAsync(catalogName, forceRefresh: true);
                }
                else
                {
                    await _catalogService.RefreshAllCatalogCachesAsync(forceRefresh: true);
                }
            }
            catch (ModelCatalogException ex)
            {
                throw new ModelValidationException($"Catalog cache refresh failed: {ex.Message}", ex);
            }
        }

        // Compile in memory: nothing is registered in a catalog, nothing is written.
        var operationResult = new OperationResult();
        var candidate = await _compilerService.CompileInMemoryAsync(rootPath, operationResult);
        Logger.LogInformation("Checking {Candidate} against its dependents", candidate.ModelId.FullName);

        var result = await _cascadeService.AnalyzeAsync(candidate, catalogName);

        Console.WriteLine();
        Console.Write(CkCascadeReport.RenderConsole(result));

        if (outputFilePath != null)
        {
            var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputFilePath));
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            await File.WriteAllTextAsync(outputFilePath, CkCascadeReport.RenderMarkdown(result));
            Logger.LogInformation("Markdown report written to '{OutputFilePath}'", outputFilePath);
        }

        var breaking = result.Dependents.Where(d => d.Verdict == CkDependentVerdict.Breaks).ToList();
        if (breaking.Count > 0)
        {
            throw new ModelValidationException(
                $"{candidate.ModelId.FullName} would break {breaking.Count} dependent(s): " +
                string.Join(", ", breaking.Select(d => d.Dependent.FullName)));
        }
    }
}
