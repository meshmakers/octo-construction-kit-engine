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
    private readonly IOptions<PublicGitHubCatalogOptions> _publicGithubOptions;
    private readonly IOptions<PrivateGitHubCatalogOptions> _privateGithubOptions;
    private readonly IConfigWriter _configWriter;
    private readonly IArgument _localCatalogPath;
    private readonly IArgument _publicGitHubRepositoryOwner;
    private readonly IArgument _publicGitHubRepositoryName;
    private readonly IArgument _publicGitHubRepositoryBranch;
    private readonly IArgument _publicGitHubApiToken;
    private readonly IArgument _publicGitHubPagesUri;

    private readonly IArgument _privateGitHubRepositoryOwner;
    private readonly IArgument _privateGitHubRepositoryName;
    private readonly IArgument _privateGitHubRepositoryBranch;
    private readonly IArgument _privateGitHubApiToken;
    private readonly IArgument _privateGitHubPagesUri;

    public ConfigCommand(ILogger<ConfigCommand> logger, IOptions<OctoToolOptions> options,
        IOptions<PublicGitHubCatalogOptions> publicGithubOptions,
        IOptions<PrivateGitHubCatalogOptions> privateGithubOptions,
        IConfigWriter configWriter)
        : base(logger, "Config", "Configures the tool.", options)
    {
        _publicGithubOptions = publicGithubOptions;
        _privateGithubOptions = privateGithubOptions;
        _configWriter = configWriter;

        _localCatalogPath = CommandArgumentValue.AddArgument("lcp", "localCatalogPath",
            ["Path of the local Construction Kit Library catalog"], false, 1);

        _publicGitHubRepositoryOwner = CommandArgumentValue.AddArgument("pgo", "publicGitHubRepositoryOwner",
            ["GitHub Repository Owner of the private GitHub catalog"], false, 1);
        _publicGitHubRepositoryName = CommandArgumentValue.AddArgument("pgr", "publicGitHubRepositoryName",
            ["GitHub Repository of the private GitHub catalog"], false, 1);
        _publicGitHubRepositoryBranch = CommandArgumentValue.AddArgument("pgb", "publicGitHubRepositoryBranch",
            ["GitHub Repository Branch of the private GitHub catalog"], false, 1);
        _publicGitHubApiToken = CommandArgumentValue.AddArgument("pgt", "publicGitHubApiToken",
            ["GitHub API token of the public GitHub catalog"], false, 1);
        _publicGitHubPagesUri = CommandArgumentValue.AddArgument("pgp", "publicGitHubPagesUri",
            ["GitHub Pages URI to read models of the public GitHub catalog"], false, 1);

        _privateGitHubRepositoryOwner = CommandArgumentValue.AddArgument("go", "privateGitHubRepositoryOwner",
            ["GitHub Repository Owner of the private GitHub catalog"], false, 1);
        _privateGitHubRepositoryName = CommandArgumentValue.AddArgument("gr", "privateGitHubRepositoryName",
            ["GitHub Repository of the private GitHub catalog"], false, 1);
        _privateGitHubRepositoryBranch = CommandArgumentValue.AddArgument("gb", "privateGitHubRepositoryBranch",
            ["GitHub Repository Branch of the private GitHub catalog"], false, 1);
        _privateGitHubApiToken = CommandArgumentValue.AddArgument("gt", "privateGitHubApiToken",
            ["GitHub API token of the private GitHub catalog"], false, 1);
        _privateGitHubPagesUri = CommandArgumentValue.AddArgument("gp", "privateGitHubPagesUri",
            ["GitHub Pages URI to read models of the private GitHub catalog"], false, 1);
    }

    public override Task Execute()
    {
        Logger.LogInformation("Configuring the tool");

        Options.Value.LocalCatalogPath = CommandArgumentValue.IsArgumentUsed(_localCatalogPath)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_localCatalogPath).ToLower()
            : null;

        ConfigurePublicGitHubCatalog();

        ConfigurePrivateGitHubCatalog();

        _configWriter.WriteSettings(Constants.OctoToolUserFolderName);

        return Task.CompletedTask;
    }

    private void ConfigurePrivateGitHubCatalog()
    {
        if (CommandArgumentValue.IsArgumentUsed(_privateGitHubRepositoryName))
        {
            _privateGithubOptions.Value.GitHubRepositoryName = CommandArgumentValue.GetArgumentScalarValue<string>(_privateGitHubRepositoryName).ToLower();
        }

        if (CommandArgumentValue.IsArgumentUsed(_privateGitHubRepositoryOwner))
        {
            _privateGithubOptions.Value.GitHubRepositoryOwner = CommandArgumentValue.GetArgumentScalarValue<string>(_privateGitHubRepositoryOwner).ToLower();
        }

        if (CommandArgumentValue.IsArgumentUsed(_privateGitHubRepositoryBranch))
        {
            _privateGithubOptions.Value.GitHubRepositoryBranch = CommandArgumentValue.GetArgumentScalarValue<string>(_privateGitHubRepositoryBranch).ToLower();
        }

        if (CommandArgumentValue.IsArgumentUsed(_privateGitHubPagesUri))
        {
            _privateGithubOptions.Value.GitHubPagesUri = CommandArgumentValue.GetArgumentScalarValue<string>(_privateGitHubPagesUri).ToLower();
        }

        if (CommandArgumentValue.IsArgumentUsed(_privateGitHubApiToken))
        {
            _privateGithubOptions.Value.GitHubApiToken =
                CommandArgumentValue.GetArgumentScalarValue<string>(_privateGitHubApiToken);
        }
    }

    private void ConfigurePublicGitHubCatalog()
    {
        if (CommandArgumentValue.IsArgumentUsed(_publicGitHubRepositoryName))
        {
            _publicGithubOptions.Value.GitHubRepositoryName = CommandArgumentValue.GetArgumentScalarValue<string>(_publicGitHubRepositoryName).ToLower();
        }

        if (CommandArgumentValue.IsArgumentUsed(_publicGitHubRepositoryOwner))
        {
            _publicGithubOptions.Value.GitHubRepositoryOwner = CommandArgumentValue.GetArgumentScalarValue<string>(_publicGitHubRepositoryOwner).ToLower();
        }

        if (CommandArgumentValue.IsArgumentUsed(_publicGitHubRepositoryBranch))
        {
            _publicGithubOptions.Value.GitHubRepositoryBranch = CommandArgumentValue.GetArgumentScalarValue<string>(_publicGitHubRepositoryBranch).ToLower();
        }

        if (CommandArgumentValue.IsArgumentUsed(_publicGitHubPagesUri))
        {
            _publicGithubOptions.Value.GitHubPagesUri = CommandArgumentValue.GetArgumentScalarValue<string>(_publicGitHubPagesUri).ToLower();
        }

        if (CommandArgumentValue.IsArgumentUsed(_publicGitHubApiToken))
        {
            _publicGithubOptions.Value.GitHubApiToken =
                CommandArgumentValue.GetArgumentScalarValue<string>(_publicGitHubApiToken);
        }
    }
}