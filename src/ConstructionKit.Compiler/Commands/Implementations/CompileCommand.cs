using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

internal class CompileCommand : CkcCommand
{
    private readonly IArgument _cachePathArg;
    private readonly IOptions<LocalFileSystemCatalogOptions> _localCatalogOptions;
    private readonly ICompilerService _compilerService;
    private readonly IArgument _pathArg;
    private readonly IArgument _outputPathArg;
    private readonly IArgument _compileResultArg;
    private readonly IArgument _localCatalogEnabled;

    public CompileCommand(ILogger<CompileCommand> logger, IOptions<OctoToolOptions> options, IOptions<LocalFileSystemCatalogOptions> localCatalogOptions,
        ICompilerService compilerService)
        : base(logger, "Compile", "Validates and creates output files for a construction kit model directory", options)
    {
        _localCatalogOptions = localCatalogOptions;
        _compilerService = compilerService;

        _pathArg = CommandArgumentValue.AddArgument("p", "path",
            ["Root path of construction kit model directory"], true, 1);

        _outputPathArg = CommandArgumentValue.AddArgument("o", "outputPath",
            ["Output path of compiled construction kit"], true, 1);

        _cachePathArg = CommandArgumentValue.AddArgument("c", "cache",
        [
            "If used, at the defined path a cache file is created containing " +
            "all dependent construction kit models."
        ], false, 1);

        _compileResultArg = CommandArgumentValue.AddArgument("cr", "compileResult",
            ["If used, the file path of compiled files is written to output"], false);

        _localCatalogEnabled = CommandArgumentValue.AddArgument("lce", "localCatalogEnabled",
            ["Enable or disable the local Construction Kit Library catalog"], false, 1);
    }

    public override async Task Execute()
    {
        await base.Execute();
        
        Logger.LogInformation("Compiling construction kit model directory");

        var rootPath = CommandArgumentValue.GetArgumentScalarValue<string>(_pathArg);
        var outputPath = CommandArgumentValue.GetArgumentScalarValue<string>(_outputPathArg);
        string? cacheFilePath = null;
        if (CommandArgumentValue.IsArgumentUsed(_cachePathArg))
        {
            cacheFilePath = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_cachePathArg);
        }

        if (CommandArgumentValue.IsArgumentUsed(_localCatalogEnabled))
        {
            var isEnabled = CommandArgumentValue.GetArgumentScalarValueOrDefault<bool>(_localCatalogEnabled);

            Logger.LogInformation("Local Construction Kit catalog is {Status}", isEnabled ? "enabled" : "disabled");
            SetLocalCatalogEnabled(isEnabled);
        }


        bool writeCompileResult = CommandArgumentValue.IsArgumentUsed(_compileResultArg);

        Logger.LogDebug("Construction Kit directory: {Path}", Path.GetFullPath(rootPath));
        Logger.LogDebug("Output directory: {Path}", Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(cacheFilePath))
        {
            Logger.LogDebug("Cache directory: {Path}", Path.GetFullPath(cacheFilePath));
        }

        try
        {
            var compileResult = await _compilerService.CompileAsync(rootPath, outputPath, cacheFilePath);
            if (writeCompileResult)
            {
                Console.WriteLine(compileResult.CompiledModelFile);
            }
        }
        catch (Exception)
        {
            Logger.LogError("Error compiling construction kit model directory \'{Path}\'", Path.GetFullPath(rootPath));
            throw;
        }

        Logger.LogInformation("Construction kit model directory compiled");
    }

    private void SetLocalCatalogEnabled(bool isEnabled)
    {
        _localCatalogOptions.Value.IsEnabled = isEnabled;
    }
}