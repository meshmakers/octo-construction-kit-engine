using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Configuration;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

internal class ConfigCommand : Command<OctoToolOptions>
{
    private readonly IOptions<GitHubCatalogOptions> _githubOptions;
    private readonly IConfigWriter _configWriter;
    private readonly IArgument _localCatalogPath;
    private readonly IArgument _gitHubRepositoryOwner;
    private readonly IArgument _gitHubRepositoryName;
    private readonly IArgument _gitHubRepositoryBranch;
    private readonly IArgument _gitHubApiToken;
    private readonly IArgument _gitHubPagesUri;

    public ConfigCommand(ILogger<ConfigCommand> logger, IOptions<OctoToolOptions> options, IOptions<GitHubCatalogOptions> githubOptions,
        IConfigWriter configWriter)
        : base(logger, "Config", "Configures the tool.", options)
    {
        _githubOptions = githubOptions;
        _configWriter = configWriter;

        _localCatalogPath = CommandArgumentValue.AddArgument("lcp", "localCatalogPath",
            ["Path of the local Construction Kit Model Repository"], false, 1);
        _gitHubRepositoryOwner = CommandArgumentValue.AddArgument("go", "gitHubRepositoryOwner",
            ["GitHub Repository Owner to publish to a GitHub Repository"], false, 1);
        _gitHubRepositoryName = CommandArgumentValue.AddArgument("gr", "gitHubRepositoryName",
            ["GitHub Repository to publish to a GitHub Repository"], false, 1);
        _gitHubRepositoryBranch = CommandArgumentValue.AddArgument("gb", "gitHubRepositoryBranch",
            ["GitHub Repository Branch to publish to a GitHub Repository"], false, 1);
        _gitHubApiToken = CommandArgumentValue.AddArgument("gt", "gitHubApiToken",
            ["GitHub API token to publish to a GitHub Repository"], false, 1);
        _gitHubPagesUri = CommandArgumentValue.AddArgument("gp", "gitHubPagesUri",
            ["GitHub Pages URI to read models from a GitHub Pages site"], false, 1);
    }

    public override Task Execute()
    {
        Logger.LogInformation("Configuring the tool");

        Options.Value.LocalCatalogPath = CommandArgumentValue.IsArgumentUsed(_localCatalogPath)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_localCatalogPath).ToLower()
            : null;

        if (CommandArgumentValue.IsArgumentUsed(_gitHubRepositoryName))
        {
            _githubOptions.Value.GitHubRepositoryName = CommandArgumentValue.GetArgumentScalarValue<string>(_gitHubRepositoryName).ToLower();
        }
        
        if (CommandArgumentValue.IsArgumentUsed(_gitHubRepositoryOwner))
        {
            _githubOptions.Value.GitHubRepositoryOwner = CommandArgumentValue.GetArgumentScalarValue<string>(_gitHubRepositoryOwner).ToLower();
        }
        
        if (CommandArgumentValue.IsArgumentUsed(_gitHubRepositoryBranch))
        {
            _githubOptions.Value.GitHubRepositoryBranch = CommandArgumentValue.GetArgumentScalarValue<string>(_gitHubRepositoryBranch).ToLower();
        }

        if (CommandArgumentValue.IsArgumentUsed(_gitHubPagesUri))
        {
            _githubOptions.Value.GitHubPagesUri = CommandArgumentValue.GetArgumentScalarValue<string>(_gitHubPagesUri).ToLower();
        }
        
        _githubOptions.Value.GitHubApiToken = CommandArgumentValue.IsArgumentUsed(_gitHubApiToken)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_gitHubApiToken)
            : null;

        _configWriter.WriteSettings(Constants.OctoToolUserFolderName);

        return Task.CompletedTask;
    }
}