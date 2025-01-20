using System.Reflection;
using Meshmakers.Common.CommandLineParser.Commands;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

internal class VersionCommand: Command<OctoToolOptions>
{
    private readonly ILogger<VersionCommand> _logger;

    public VersionCommand(ILogger<VersionCommand> logger, IOptions<OctoToolOptions> options) 
        : base(logger, "version", "Returns the copyright and version of the tool.", options)
    {
        _logger = logger;
    }

    public override Task Execute()
    {
        _logger.LogInformation("Octo Mesh Construction Kit Compiler, Version {ProductVersion}",
            GetProductVersion());
        _logger.LogInformation("{Copyright}", GetCopyright());

        return Task.CompletedTask;
    }
    
    private static string GetProductVersion()
    {
        var attribute = Assembly
            .GetExecutingAssembly()
            .GetCustomAttributes<AssemblyFileVersionAttribute>()
            .Single();
        return attribute.Version;
    }

    private static string GetCopyright()
    {
        var attribute = Assembly
            .GetExecutingAssembly()
            .GetCustomAttributes<AssemblyCopyrightAttribute>()
            .SingleOrDefault();

        if (attribute == null)
        {
            return "Development Version";
        }

        return attribute.Copyright;
    }
}