using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

internal class GetReposCommand : Command<OctoToolOptions>
{
    private readonly ICkModelRepositoryManager _ckModelRepositoryManager;
    
    public GetReposCommand(ILogger<GetReposCommand> logger, IOptions<OctoToolOptions> options,
        ICkModelRepositoryManager ckModelRepositoryManager)
        : base(logger, "GetRepos", "Gets a list of known Construction Kit Repositories", options)
    {
        _ckModelRepositoryManager = ckModelRepositoryManager;

    }

    public override Task Execute()
    {
        Logger.LogInformation("Construction Kit Model Repositories:");

        var list = _ckModelRepositoryManager.GetRepositoryList();
        foreach (var tuple in list)
        {
            Logger.LogInformation("- '{Name}': {Description}", tuple.Item1, tuple.Item2);
        }

        return Task.CompletedTask;
    }
}
