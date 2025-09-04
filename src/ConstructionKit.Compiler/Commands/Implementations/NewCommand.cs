using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

internal class NewCommand : Command<OctoToolOptions>
{
    private readonly ICompilerService _compilerService;
    private readonly IArgument _pathArg;

    public NewCommand(ILogger<NewCommand> logger, IOptions<OctoToolOptions> options,
        ICompilerService compilerService)
        : base(logger, "New", "Creates new construction kit model directory", options)
    {
        _compilerService = compilerService;

        _pathArg = CommandArgumentValue.AddArgument("p", "path",
            ["Root path of construction kit model directory"], true, 1);
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Creating construction kit model directory");

        var rootPath = CommandArgumentValue.GetArgumentScalarValue<string>(_pathArg);
        Logger.LogInformation("Path of root directory: {Path}", rootPath);

        await _compilerService.CreateNewAsync(rootPath);

        Logger.LogInformation("Construction kit model directory created");
    }
}