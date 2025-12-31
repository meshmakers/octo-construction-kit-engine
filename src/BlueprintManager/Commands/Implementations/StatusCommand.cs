using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.BlueprintManager.Commands.Implementations;

/// <summary>
/// Command to show current blueprint version and available updates for a tenant.
/// </summary>
internal class StatusCommand : Command<BpmToolOptions>
{
    private readonly IBlueprintService _blueprintService;
    private readonly IArgument _tenantArg;

    public StatusCommand(
        ILogger<StatusCommand> logger,
        IOptions<BpmToolOptions> options,
        IBlueprintService blueprintService)
        : base(logger, "status", "Shows current blueprint version and available updates for a tenant", options)
    {
        _blueprintService = blueprintService;

        _tenantArg = CommandArgumentValue.AddArgument("t", "tenant",
            ["Tenant identifier"], true, 1);
    }

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantArg);

        Logger.LogInformation("Checking blueprint status for tenant: {TenantId}", tenantId);
        Logger.LogInformation("");

        // Get current blueprint info
        var history = await _blueprintService.GetHistoryAsync(tenantId);

        if (history.Count == 0)
        {
            Logger.LogInformation("No blueprint has been applied to this tenant");
            return;
        }

        var current = history[^1]; // Last entry is current
        Logger.LogInformation("Current Blueprint:");
        Logger.LogInformation("  Version:    {BlueprintId}", current.BlueprintId.FullName);
        Logger.LogInformation("  Applied:    {AppliedAt:yyyy-MM-dd HH:mm:ss}", current.AppliedAt);
        Logger.LogInformation("  Mode:       {Mode}", current.ApplicationMode);

        if (current.PreviousVersion != null)
        {
            Logger.LogInformation("  Previous:   {PreviousVersion}", current.PreviousVersion.FullName);
        }

        Logger.LogInformation("  Entities:   {Created} created, {Updated} updated, {Deleted} deleted",
            current.EntitiesCreated, current.EntitiesUpdated, current.EntitiesDeleted);

        Logger.LogInformation("");

        // Check for updates
        var updateInfo = await _blueprintService.GetUpdateInfoAsync(tenantId);

        if (updateInfo == null)
        {
            Logger.LogInformation("Unable to check for updates");
            return;
        }

        if (updateInfo.AvailableVersions.Count == 0)
        {
            Logger.LogInformation("Blueprint is up to date. No updates available");
            return;
        }

        Logger.LogInformation("Available Updates:");
        foreach (var version in updateInfo.AvailableVersions)
        {
            var recommended = version.Equals(updateInfo.RecommendedVersion) ? " (recommended)" : "";
            Logger.LogInformation("  - {Version}{Recommended}", version.FullName, recommended);
        }

        Logger.LogInformation("");

        if (updateInfo.HasMigrationPath)
        {
            Logger.LogInformation("Migration path available from current version");
        }
        else
        {
            Logger.LogInformation("No direct migration path - use Merge or Full mode");
        }

        if (updateInfo.AvailableMigrations?.Count > 0)
        {
            Logger.LogInformation("");
            Logger.LogInformation("Available Migrations from versions:");
            foreach (var fromVersion in updateInfo.AvailableMigrations)
            {
                Logger.LogInformation("  - {FromVersion}", fromVersion);
            }
        }

        Logger.LogInformation("");
        Logger.LogInformation("Use 'preview' to preview changes before updating");
        Logger.LogInformation("Use 'update' to apply an update");
    }
}
