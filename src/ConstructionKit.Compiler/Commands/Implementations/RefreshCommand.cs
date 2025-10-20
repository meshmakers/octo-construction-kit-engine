using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

internal class RefreshCommand : CkcCommand
{
    private readonly ICatalogService _catalogService;
    private readonly IArgument _catalogArg;

    public RefreshCommand(ILogger<RefreshCommand> logger, IOptions<OctoToolOptions> options,
        ICatalogService catalogService)
        : base(logger, "RefreshCatalogCache", "Refreshes the catalog cache", options)
    {
        _catalogService = catalogService;

        _catalogArg = CommandArgumentValue.AddArgument("c", "catalog",
            ["Name of the construction kit catalog. By default 'LocalFileSystemCatalog' is used."], 1);
    }

    public override async Task Execute()
    {
        await base.Execute();

        Logger.LogInformation("Refreshing catalog cache");

        var catalogName = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_catalogArg) ??
                          "LocalFileSystemCatalog";
        Logger.LogInformation("Catalog '{CatalogName}'", catalogName);


        await _catalogService.RefreshCatalogCacheAsync(catalogName);

        Logger.LogInformation("Catalog cache refreshed");
    }
}