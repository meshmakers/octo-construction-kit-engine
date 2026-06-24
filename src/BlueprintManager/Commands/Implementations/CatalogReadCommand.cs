using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.BlueprintManager.Commands.Implementations;

/// <summary>
/// Base for commands that read blueprint data from the catalogs (list/search/get and the unpublish
/// dry-run). Those reads go through an on-disk cache that is otherwise only rebuilt on publish or when the
/// cache file is missing — so without intervention they can serve stale data (a freshly published version
/// stays invisible until something else invalidates the cache).
/// </summary>
/// <remarks>
/// This base forces a catalog refresh in <see cref="PreValidate" /> — the hook
/// <c>CommandParser.ParseAndValidateAsync</c> invokes before <see cref="Command{TOptions}.Execute" /> — so
/// every read command observes the current catalog state. The refresh is resilient: a catalog that cannot
/// be reached logs/returns empty rather than throwing.
/// </remarks>
internal abstract class CatalogReadCommand : Command<BpmToolOptions>
{
    /// <summary>
    /// The catalog manager, shared with derived read commands.
    /// </summary>
    protected IBlueprintCatalogManager CatalogManager { get; }

    protected CatalogReadCommand(
        ILogger<Command<BpmToolOptions>> logger,
        string commandValue,
        string commandDescription,
        IOptions<BpmToolOptions> options,
        IBlueprintCatalogManager catalogManager)
        : base(logger, commandValue, commandDescription, options)
    {
        CatalogManager = catalogManager;
    }

    /// <inheritdoc />
    public override Task PreValidate()
    {
        return CatalogManager.RefreshAllCatalogCachesAsync(force: true);
    }
}
