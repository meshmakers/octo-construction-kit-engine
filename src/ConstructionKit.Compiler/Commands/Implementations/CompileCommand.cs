using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

internal class CompileCommand : Command<OctoToolOptions>
{
    private readonly ICompilerService _compilerService;
    private readonly IArgument _pathArg;
    private readonly IArgument _cacheArg;

    public CompileCommand(ILogger<CompileCommand> logger, IOptions<OctoToolOptions> options,
        ICompilerService compilerService)
        : base(logger, "Compile", "Validates and creates output files for a construction kit model directory", options)
    {
        _compilerService = compilerService;

        _pathArg = CommandArgumentValue.AddArgument("p", "path",
            new[] { "Root path of construction kit model directory" }, true, 1);
        
        _cacheArg = CommandArgumentValue.AddArgument("c", "cache",
            new[] { "If used, parallel to the compiled construction kit model a cache file is created containing " +
                    "all dependent construction kit models." }, false);
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Compiling construction kit model directory");

        var rootPath = CommandArgumentValue.GetArgumentScalarValue<string>(_pathArg);
        var createCacheFile = CommandArgumentValue.IsArgumentUsed(_cacheArg);
        Logger.LogInformation("Path of root directory: {Path}", rootPath);

        await _compilerService.CompileAsync(rootPath, createCacheFile);
        
        Logger.LogInformation("Construction kit model directory compiled");
    }
}
