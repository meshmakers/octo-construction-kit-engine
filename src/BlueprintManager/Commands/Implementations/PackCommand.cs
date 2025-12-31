using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.BlueprintManager.Commands.Implementations;

/// <summary>
/// Command to pack a blueprint directory into a distributable archive.
/// </summary>
internal class PackCommand : Command<BpmToolOptions>
{
    private readonly IBlueprintCompilerService _blueprintCompilerService;
    private readonly IArgument _pathArg;
    private readonly IArgument _outputArg;

    public PackCommand(
        ILogger<PackCommand> logger,
        IOptions<BpmToolOptions> options,
        IBlueprintCompilerService blueprintCompilerService)
        : base(logger, "pack", "Packs a blueprint directory into a distributable archive", options)
    {
        _blueprintCompilerService = blueprintCompilerService;

        _pathArg = CommandArgumentValue.AddArgument("p", "path",
            ["Path to the blueprint directory"], true, 1);

        _outputArg = CommandArgumentValue.AddArgument("o", "output",
            ["Output directory for the packed blueprint"], true, 1);
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Packing blueprint");

        var path = CommandArgumentValue.GetArgumentScalarValue<string>(_pathArg);
        var outputPath = CommandArgumentValue.GetArgumentScalarValue<string>(_outputArg);

        Logger.LogInformation("Path: {Path}", path);
        Logger.LogInformation("Output: {Output}", outputPath);

        var operationResult = new OperationResult();
        var zipPath = await _blueprintCompilerService.PackAsync(path, outputPath, operationResult);

        Logger.LogInformation("Blueprint packed successfully: {ZipPath}", zipPath);
    }
}
