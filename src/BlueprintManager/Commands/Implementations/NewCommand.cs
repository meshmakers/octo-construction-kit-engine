using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.BlueprintManager.Commands.Implementations;

/// <summary>
/// Command to create a new blueprint directory with template files.
/// </summary>
internal class NewCommand : Command<BpmToolOptions>
{
    private readonly IBlueprintCompilerService _blueprintCompilerService;
    private readonly IArgument _pathArg;
    private readonly IArgument _nameArg;
    private readonly IArgument _versionArg;

    public NewCommand(
        ILogger<NewCommand> logger,
        IOptions<BpmToolOptions> options,
        IBlueprintCompilerService blueprintCompilerService)
        : base(logger, "new", "Creates a new blueprint directory with template files", options)
    {
        _blueprintCompilerService = blueprintCompilerService;

        _pathArg = CommandArgumentValue.AddArgument("p", "path",
            ["Root path where the blueprint directory will be created"], true, 1);

        _nameArg = CommandArgumentValue.AddArgument("n", "name",
            ["Name of the blueprint (e.g., 'InfrastructureStarter')"], true, 1);

        _versionArg = CommandArgumentValue.AddArgument("v", "version",
            ["Version of the blueprint (default: 1.0.0)"], false, 1);
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Creating new blueprint");

        var path = CommandArgumentValue.GetArgumentScalarValue<string>(_pathArg);
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg);
        var version = CommandArgumentValue.IsArgumentUsed(_versionArg)
            ? CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_versionArg) ?? "1.0.0"
            : "1.0.0";

        Logger.LogInformation("Path: {Path}, Name: {Name}, Version: {Version}", path, name, version);

        await _blueprintCompilerService.CreateNewAsync(path, name, version);

        Logger.LogInformation("Blueprint '{Name}-{Version}' created successfully at {Path}", name, version, path);
    }
}
