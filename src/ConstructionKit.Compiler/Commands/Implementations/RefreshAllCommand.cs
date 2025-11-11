using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

internal class RefreshAllCommand : CkcCommand
{
    private readonly ICatalogService _catalogService;

    public RefreshAllCommand(ILogger<RefreshAllCommand> logger, IOptions<OctoToolOptions> options,
        ICatalogService catalogService)
        : base(logger, "RefreshAllCatalogCaches", "Refreshes the catalog cache of all catalogs which support caching", options)
    {
        _catalogService = catalogService;
    }

    public override async Task Execute()
    {
        await base.Execute();

        Logger.LogInformation("Refreshing catalog cache of all catalogs");

        await _catalogService.RefreshAllCatalogCachesAsync();

        Logger.LogInformation("All catalog cache refreshed");
    }
}