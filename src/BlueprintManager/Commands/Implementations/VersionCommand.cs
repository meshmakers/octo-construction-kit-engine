using System.Reflection;
using Meshmakers.Common.CommandLineParser.Commands;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.BlueprintManager.Commands.Implementations;

/// <summary>
/// Command to display version information.
/// </summary>
internal class VersionCommand : Command<BpmToolOptions>
{
    public VersionCommand(
        ILogger<VersionCommand> logger,
        IOptions<BpmToolOptions> options)
        : base(logger, "version", "Displays the version of the Blueprint Manager", options)
    {
    }

    public override Task Execute()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString() ?? "unknown";
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? version;

        Logger.LogInformation("Octo Blueprint Manager (octo-bpm)");
        Logger.LogInformation("Version: {Version}", informationalVersion);

        return Task.CompletedTask;
    }
}
