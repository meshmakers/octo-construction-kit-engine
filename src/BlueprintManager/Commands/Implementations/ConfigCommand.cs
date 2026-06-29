using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Configuration;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.BlueprintManager.Commands.Implementations;

/// <summary>
/// Command to configure the octo-bpm tool settings.
/// </summary>
internal class ConfigCommand : Command<BpmToolOptions>
{
    private readonly IOptions<LocalFileSystemBlueprintCatalogOptions> _localCatalogOptions;
    private readonly IOptions<PublicGitHubBlueprintCatalogOptions> _publicGithubOptions;
    private readonly IOptions<PrivateGitHubBlueprintCatalogOptions> _privateGithubOptions;
    private readonly IConfigWriter _configWriter;
    private readonly IArgument _localCatalogPath;
    private readonly IArgument _localCatalogEnabled;
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

    public ConfigCommand(ILogger<ConfigCommand> logger, IOptions<BpmToolOptions> options,
        IOptions<LocalFileSystemBlueprintCatalogOptions> localCatalogOptions,
        IOptions<PublicGitHubBlueprintCatalogOptions> publicGithubOptions,
        IOptions<PrivateGitHubBlueprintCatalogOptions> privateGithubOptions,
        IConfigWriter configWriter)
        : base(logger, "config", "Configures the tool", options)
    {
        _localCatalogOptions = localCatalogOptions;
        _publicGithubOptions = publicGithubOptions;
        _privateGithubOptions = privateGithubOptions;
        _configWriter = configWriter;

        _localCatalogPath = CommandArgumentValue.AddArgument("lcp", "localCatalogPath",
            ["Path of the local Blueprint catalog"], false, 1);
        _localCatalogEnabled = CommandArgumentValue.AddArgument("lce", "localCatalogEnabled",
            ["Enable or disable the local Blueprint catalog"], false, 1);

        _publicGitHubRepositoryOwner = CommandArgumentValue.AddArgument("pgo", "publicGitHubRepositoryOwner",
            ["GitHub Repository Owner of the public GitHub catalog"], false, 1);
        _publicGitHubRepositoryName = CommandArgumentValue.AddArgument("pgr", "publicGitHubRepositoryName",
            ["GitHub Repository of the public GitHub catalog"], false, 1);
        _publicGitHubRepositoryBranch = CommandArgumentValue.AddArgument("pgb", "publicGitHubRepositoryBranch",
            ["GitHub Repository Branch of the public GitHub catalog"], false, 1);
        _publicGitHubApiToken = CommandArgumentValue.AddArgument("pgt", "publicGitHubApiToken",
            ["GitHub API token of the public GitHub catalog"], false, 1);
        _publicGitHubPagesUri = CommandArgumentValue.AddArgument("pgp", "publicGitHubPagesUri",
            ["GitHub Pages URI to read blueprints of the public GitHub catalog"], false, 1);

        _privateGitHubRepositoryOwner = CommandArgumentValue.AddArgument("go", "privateGitHubRepositoryOwner",
            ["GitHub Repository Owner of the private GitHub catalog"], false, 1);
        _privateGitHubRepositoryName = CommandArgumentValue.AddArgument("gr", "privateGitHubRepositoryName",
            ["GitHub Repository of the private GitHub catalog"], false, 1);
        _privateGitHubRepositoryBranch = CommandArgumentValue.AddArgument("gb", "privateGitHubRepositoryBranch",
            ["GitHub Repository Branch of the private GitHub catalog"], false, 1);
        _privateGitHubApiToken = CommandArgumentValue.AddArgument("gt", "privateGitHubApiToken",
            ["GitHub API token of the private GitHub catalog"], false, 1);
        _privateGitHubPagesUri = CommandArgumentValue.AddArgument("gp", "privateGitHubPagesUri",
            ["GitHub Pages URI to read blueprints of the private GitHub catalog"], false, 1);
    }

    public override Task Execute()
    {
        Logger.LogInformation("Configuring the tool");

        if (CommandArgumentValue.IsArgumentUsed(_localCatalogPath))
        {
            Logger.LogInformation("Configuring local catalog path");
            // Use ApplyRootPath (not a raw .ToLower() assignment): it preserves case — on
            // case-sensitive file systems a lower-cased path is a different directory (instant
            // split-brain) — and co-locates the catalog cache with the content under the new root.
            _localCatalogOptions.Value.ApplyRootPath(
                CommandArgumentValue.GetArgumentScalarValue<string>(_localCatalogPath));
        }
        if (CommandArgumentValue.IsArgumentUsed(_localCatalogEnabled))
        {
            Logger.LogInformation("Configuring local catalog enabled state");
            _localCatalogOptions.Value.IsEnabled =
                CommandArgumentValue.GetArgumentScalarValueOrDefault<bool>(_localCatalogEnabled);
        }

        ConfigurePublicGitHubCatalog();

        ConfigurePrivateGitHubCatalog();

        _configWriter.WriteSettings(Constants.BpmToolUserFolderName);

        return Task.CompletedTask;
    }

    private void ConfigurePrivateGitHubCatalog()
    {
        if (CommandArgumentValue.IsArgumentUsed(_privateGitHubRepositoryName))
        {
            Logger.LogInformation("Configuring private GitHub catalog repository name");
            _privateGithubOptions.Value.GitHubRepositoryName = CommandArgumentValue.GetArgumentScalarValue<string>(_privateGitHubRepositoryName).ToLower();
        }

        if (CommandArgumentValue.IsArgumentUsed(_privateGitHubRepositoryOwner))
        {
            Logger.LogInformation("Configuring private GitHub catalog repository owner");
            _privateGithubOptions.Value.GitHubRepositoryOwner = CommandArgumentValue.GetArgumentScalarValue<string>(_privateGitHubRepositoryOwner).ToLower();
        }

        if (CommandArgumentValue.IsArgumentUsed(_privateGitHubRepositoryBranch))
        {
            Logger.LogInformation("Configuring private GitHub catalog repository branch");
            _privateGithubOptions.Value.GitHubRepositoryBranch = CommandArgumentValue.GetArgumentScalarValue<string>(_privateGitHubRepositoryBranch).ToLower();
        }

        if (CommandArgumentValue.IsArgumentUsed(_privateGitHubPagesUri))
        {
            Logger.LogInformation("Configuring private GitHub catalog pages URI");
            _privateGithubOptions.Value.GitHubPagesUri = CommandArgumentValue.GetArgumentScalarValue<string>(_privateGitHubPagesUri).ToLower();
        }

        if (CommandArgumentValue.IsArgumentUsed(_privateGitHubApiToken))
        {
            Logger.LogInformation("Configuring private GitHub catalog API token");
            _privateGithubOptions.Value.GitHubApiToken =
                CommandArgumentValue.GetArgumentScalarValue<string>(_privateGitHubApiToken);
        }
    }

    private void ConfigurePublicGitHubCatalog()
    {
        if (CommandArgumentValue.IsArgumentUsed(_publicGitHubRepositoryName))
        {
            Logger.LogInformation("Configuring public GitHub catalog repository name");
            _publicGithubOptions.Value.GitHubRepositoryName = CommandArgumentValue.GetArgumentScalarValue<string>(_publicGitHubRepositoryName).ToLower();
        }

        if (CommandArgumentValue.IsArgumentUsed(_publicGitHubRepositoryOwner))
        {
            Logger.LogInformation("Configuring public GitHub catalog repository owner");
            _publicGithubOptions.Value.GitHubRepositoryOwner = CommandArgumentValue.GetArgumentScalarValue<string>(_publicGitHubRepositoryOwner).ToLower();
        }

        if (CommandArgumentValue.IsArgumentUsed(_publicGitHubRepositoryBranch))
        {
            Logger.LogInformation("Configuring public GitHub catalog branch");
            _publicGithubOptions.Value.GitHubRepositoryBranch = CommandArgumentValue.GetArgumentScalarValue<string>(_publicGitHubRepositoryBranch).ToLower();
        }

        if (CommandArgumentValue.IsArgumentUsed(_publicGitHubPagesUri))
        {
            Logger.LogInformation("Configuring public GitHub catalog pages URI");
            _publicGithubOptions.Value.GitHubPagesUri = CommandArgumentValue.GetArgumentScalarValue<string>(_publicGitHubPagesUri).ToLower();
        }

        if (CommandArgumentValue.IsArgumentUsed(_publicGitHubApiToken))
        {
            Logger.LogInformation("Configuring public GitHub catalog API token");
            _publicGithubOptions.Value.GitHubApiToken =
                CommandArgumentValue.GetArgumentScalarValue<string>(_publicGitHubApiToken);
        }
    }
}
