using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

internal class ConfigCommand : Command<OctoToolOptions>
{
    private readonly IArgument _localCkModelRepoPath;
    private readonly IConfigWriter _configWriter;

    public ConfigCommand(ILogger<ConfigCommand> logger, IOptions<OctoToolOptions> options,
        IConfigWriter configWriter)
        : base(logger, "Config", "Configures the tool.", options)
    {
        _configWriter = configWriter;

        _localCkModelRepoPath = CommandArgumentValue.AddArgument("lp", "localCkModelRepoPath",
            new[] { "Path of the local Construction Kit Model Repository" }, 1);
      
    }

    public override Task Execute()
    {
        Logger.LogInformation("Configuring the tool");

        if (CommandArgumentValue.IsArgumentUsed(_localCkModelRepoPath))
        {
            Options.Value.LocalCkModelRepositoryPath = CommandArgumentValue.GetArgumentScalarValue<string>(_localCkModelRepoPath).ToLower();
        }
        else
        {
            Options.Value.LocalCkModelRepositoryPath = null;
        }

        _configWriter.WriteSettings(Constants.OctoToolUserFolderName);

        return Task.CompletedTask;
    }
}
