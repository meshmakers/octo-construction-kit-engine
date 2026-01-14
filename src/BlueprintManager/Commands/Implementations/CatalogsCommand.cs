using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.BlueprintManager.Commands.Implementations;

/// <summary>
/// Command to list available blueprint catalogs.
/// </summary>
internal class CatalogsCommand : Command<BpmToolOptions>
{
    private readonly IBlueprintCatalogManager _catalogManager;

    public CatalogsCommand(
        ILogger<CatalogsCommand> logger,
        IOptions<BpmToolOptions> options,
        IBlueprintCatalogManager catalogManager)
        : base(logger, "catalogs", "Lists available blueprint catalogs", options)
    {
        _catalogManager = catalogManager;
    }

    public override Task Execute()
    {
        Logger.LogInformation("Blueprint catalogs:");

        var list = _catalogManager.GetCatalogList();
        foreach (var tuple in list)
        {
            Logger.LogInformation("- '{Name}': {Description}", tuple.Item1, tuple.Item2);
        }

        return Task.CompletedTask;
    }
}
