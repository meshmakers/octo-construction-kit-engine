using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NLog;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands;

/// <summary>
/// Internal base class for all commands.
/// </summary>
internal abstract class CkcCommand : Command<OctoToolOptions>
{
    private readonly IArgument _verboseArg;

    protected CkcCommand(ILogger<CkcCommand> logger, string commandValue, string commandDescription,
        IOptions<OctoToolOptions> options) : base(logger, commandValue, commandDescription, options)
    {
        _verboseArg = CommandArgumentValue.AddArgument("v", "verbose",
            ["Defines the verbose level: 'None' for no output, 'Detailed' for trace and debug output, otherwise 'Default'"], false, 1);
    }

    public override Task Execute()
    {
        if (CommandArgumentValue.IsArgumentUsed(_verboseArg))
        {
            var level = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_verboseArg);
            if (!string.IsNullOrWhiteSpace(level))
            {
                switch (level.ToLower())
                {
                    case "none":
                        DisableOutput();
                        break;
                    case "detailed":
                        DetailedOutput();
                        break;
                }
            }
        }

        return Task.CompletedTask;
    }
    
    private void DetailedOutput()
    {
        if (LogManager.Configuration == null)
        {
            return;
        }
        foreach (var rule in LogManager.Configuration.LoggingRules)
        {
            rule.EnableLoggingForLevels(NLog.LogLevel.Trace, NLog.LogLevel.Info);
        }

        LogManager.ReconfigExistingLoggers();
    }
    
    private void DisableOutput()
    {
        if (LogManager.Configuration == null)
        {
            return;
        }
        foreach (var rule in LogManager.Configuration.LoggingRules)
        {
            rule.DisableLoggingForLevels(NLog.LogLevel.Trace, NLog.LogLevel.Info);
        }

        LogManager.ReconfigExistingLoggers();
    }
}