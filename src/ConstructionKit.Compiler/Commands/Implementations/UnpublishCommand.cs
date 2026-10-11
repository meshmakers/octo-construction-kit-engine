using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

/// <summary>
///     Withdraws a published model version from a catalog (AB#5433), the inverse of <see cref="PublishCommand" /> and
///     the model-side twin of <c>octo-bpm unpublish</c>. Dry run by default: prints what would be removed and which
///     published models depend on it. <c>--force</c> applies. The version number is burned: it is recorded as
///     withdrawn and never published again. Refused (also with <c>--force</c>) while a published model of the same
///     catalog depends on the version. The public catalog additionally needs <c>--reason</c>, which is written to the
///     commit message of the catalog change.
/// </summary>
internal class UnpublishCommand : CkcCommand
{
    private readonly ICatalogService _catalogService;
    private readonly IOptions<LocalFileSystemCatalogOptions> _localCatalogOptions;
    private readonly IArgument _modelArg;
    private readonly IArgument _versionArg;
    private readonly IArgument _catalogArg;
    private readonly IArgument _forceArg;
    private readonly IArgument _reasonArg;
    private readonly IArgument _localCatalogRoot;

    public UnpublishCommand(ILogger<UnpublishCommand> logger, IOptions<OctoToolOptions> options,
        ICatalogService catalogService, IOptions<LocalFileSystemCatalogOptions> localCatalogOptions)
        : base(logger, "Unpublish",
            "Withdraws a published construction kit model version from a catalog; the version number is burned", options)
    {
        _catalogService = catalogService;
        _localCatalogOptions = localCatalogOptions;

        _modelArg = CommandArgumentValue.AddArgument("m", "model",
            ["Name of the construction kit model (e.g. 'System')"], true, 1);

        _versionArg = CommandArgumentValue.AddArgument("ve", "version",
            ["Exact version to withdraw (e.g. '2.3.0')"], true, 1);

        _catalogArg = CommandArgumentValue.AddArgument("cn", "catalogName",
            ["Name of the catalog to withdraw the version from. By default 'LocalFileSystemCatalog' is used."], false, 1);

        _forceArg = CommandArgumentValue.AddArgument("f", "force",
            ["Actually withdraw the version. Without this flag the command only prints what would happen (dry run)."],
            false, 0);

        _reasonArg = CommandArgumentValue.AddArgument("rs", "reason",
            [
                "Why the version is withdrawn. Mandatory for the public catalog ('PublicGitHubCatalog'); written to the",
                "commit message of the catalog change and to the withdrawn record."
            ], false, 1);

        _localCatalogRoot = CommandArgumentValue.AddArgument("lcr", "localCatalogRoot",
            ["Root path of the local Construction Kit Library catalog for this invocation only (not persisted)"],
            false, 1);
    }

    public override async Task Execute()
    {
        await base.Execute();

        var modelName = CommandArgumentValue.GetArgumentScalarValue<string>(_modelArg);
        var versionText = CommandArgumentValue.GetArgumentScalarValue<string>(_versionArg);
        var catalogName = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_catalogArg) ??
                          LocalFileSystemCatalog.Name;
        var isForced = CommandArgumentValue.IsArgumentUsed(_forceArg);
        var reason = CommandArgumentValue.IsArgumentUsed(_reasonArg)
            ? CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_reasonArg)
            : null;

        if (CommandArgumentValue.IsArgumentUsed(_localCatalogRoot))
        {
            _localCatalogOptions.Value.ApplyRootPath(
                CommandArgumentValue.GetArgumentScalarValue<string>(_localCatalogRoot));
        }

        CkModelId modelId;
        try
        {
            modelId = new CkModelId(modelName, new CkVersion(versionText));
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            throw new ModelValidationException($"Invalid version '{versionText}'. Expected 'Major.Minor.Revision' (e.g. 2.3.0).", ex);
        }

        try
        {
            var plan = await _catalogService.PlanUnpublishAsync(catalogName, modelId);
            if (!plan.IsPublished)
            {
                if (!isForced)
                {
                    Logger.LogInformation(
                        "Dry run - '{ModelId}' is not published in catalog '{Catalog}' (never published or already unpublished). Nothing to do.",
                        modelId.FullName, catalogName);
                    return;
                }
            }
            else if (plan.Dependents.Count > 0)
            {
                var dependentList = string.Join(", ",
                    plan.Dependents.Select(d => $"{d.ModelId.FullName} (needs {d.Range})"));
                throw new ModelValidationException(
                    $"Cannot unpublish '{modelId.FullName}' from catalog '{catalogName}': {plan.Dependents.Count} published " +
                    $"model(s) depend on it and would be left with an unresolvable dependency: {dependentList}. " +
                    "Unpublish or replace the dependents first.");
            }
            else if (!isForced)
            {
                Logger.LogWarning(
                    "Dry run - '{ModelId}' would be PERMANENTLY removed from catalog '{Catalog}' and its version number burned " +
                    "(never publishable again). No published model depends on it. Re-run with --force to apply.",
                    modelId.FullName, catalogName);
                return;
            }

            var result = await _catalogService.UnpublishAsync(catalogName, modelId, reason);
            Logger.LogInformation(
                result == CatalogUnpublishResult.Removed
                    ? "Unpublished '{ModelId}' from catalog '{Catalog}'; the version number is burned."
                    : "'{ModelId}' was already unpublished from catalog '{Catalog}'; nothing changed.",
                modelId.FullName, catalogName);
        }
        catch (ModelCatalogException ex)
        {
            throw new ModelValidationException(ex.Message, ex);
        }
    }
}
