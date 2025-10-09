using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

internal class GetReposCommand : Command<OctoToolOptions>
{
    private readonly ICatalogService _catalogService;

    public GetReposCommand(ILogger<GetReposCommand> logger, IOptions<OctoToolOptions> options,
        ICatalogService catalogService)
        : base(logger, "GetRepos", "Gets a list of known Construction Kit Repositories", options)
    {
        _catalogService = catalogService;
    }

    public override Task Execute()
    {
        Logger.LogInformation("Construction Kit Model Repositories:");

        var list = _catalogService.GetCatalogList();
        foreach (var tuple in list)
        {
            Logger.LogInformation("- '{Name}': {Description}", tuple.Item1, tuple.Item2);
        }

        return Task.CompletedTask;
    }
}