using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

internal class FindCommand : Command<OctoToolOptions>
{
    private readonly ICatalogService _catalogService;
    private readonly IArgument _modelIdArgument;

    public FindCommand(ILogger<FindCommand> logger, IOptions<OctoToolOptions> options,
        ICatalogService catalogService)
        : base(logger, "Find", "Lists repositories a construction kit model has been found.", options)
    {
        _catalogService = catalogService;

        _modelIdArgument = CommandArgumentValue.AddArgument("id", "modelId",
            ["Model Id of construction kit to find, including version ranges"], true, 1);
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Finding construction kit model");

        var modelId = CommandArgumentValue.GetArgumentScalarValue<string>(_modelIdArgument);
        Logger.LogInformation("Name: {Name}", modelId);

        var repositoryList = _catalogService.GetCatalogList();
        foreach (var repository in repositoryList)
        {
            var r = await _catalogService.IsExistingAsync(repository.Item1, modelId);
            if (r.Exists)
            {
                Logger.LogInformation("Found model {Name} in repository '{Repository}'", r.ModelId,
                    repository.Item1);
            }
            else
            {
                Logger.LogInformation("Model not found in repository '{Repository}'", repository.Item1);
            }
        }

        Logger.LogInformation("All repositories checked");
    }
}