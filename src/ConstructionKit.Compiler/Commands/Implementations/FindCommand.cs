using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

internal class FindCommand : Command<OctoToolOptions>
{
    private readonly ICkModelRepositoryService _ckModelRepositoryService;
    private readonly IArgument _modelIdArgument;

    public FindCommand(ILogger<FindCommand> logger, IOptions<OctoToolOptions> options,
        ICkModelRepositoryService ckModelRepositoryService)
        : base(logger, "Find", "Lists repositories a construction kit model has been found.", options)
    {
        _ckModelRepositoryService = ckModelRepositoryService;

        _modelIdArgument = CommandArgumentValue.AddArgument("mid", "modelId",
            new[] { "Model Id of construction kit to find" }, true, 1);
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Finding construction kit model");

        var modelId = CommandArgumentValue.GetArgumentScalarValue<string>(_modelIdArgument);
        Logger.LogInformation("ModelId: {ModelId}", modelId);

        var repositoryList = _ckModelRepositoryService.GetRepositoryList();
        foreach (var repository in repositoryList)
        {
            var r = await _ckModelRepositoryService.IsCkModelExistingAsync(repository.Item1, modelId);
            if (r)
            {
                Logger.LogInformation("Found model in repository '{Repository}'", repository.Item1);
            }
        }

        Logger.LogInformation("All repositories checked");
    }
}