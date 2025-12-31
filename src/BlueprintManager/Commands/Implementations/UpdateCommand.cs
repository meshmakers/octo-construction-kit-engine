using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.BlueprintManager.Commands.Implementations;

/// <summary>
/// Command to apply a blueprint update to a tenant.
/// </summary>
internal class UpdateCommand : Command<BpmToolOptions>
{
    private readonly IBlueprintService _blueprintService;
    private readonly IArgument _tenantArg;
    private readonly IArgument _versionArg;
    private readonly IArgument _modeArg;
    private readonly IArgument _dryRunArg;
    private readonly IArgument _noBackupArg;
    private readonly IArgument _forceArg;

    public UpdateCommand(
        ILogger<UpdateCommand> logger,
        IOptions<BpmToolOptions> options,
        IBlueprintService blueprintService)
        : base(logger, "update", "Applies a blueprint update to a tenant", options)
    {
        _blueprintService = blueprintService;

        _tenantArg = CommandArgumentValue.AddArgument("t", "tenant",
            ["Tenant identifier"], true, 1);

        _versionArg = CommandArgumentValue.AddArgument("b", "blueprint",
            ["Target blueprint version (e.g., 'MyBlueprint-2.0.0')"], true, 1);

        _modeArg = CommandArgumentValue.AddArgument("m", "mode",
            ["Update mode: Safe, Merge, Full, or Migration (default: Merge)"], false, 1);

        _dryRunArg = CommandArgumentValue.AddArgument("d", "dry-run",
            ["Simulate the update without making changes"], false, 0);

        _noBackupArg = CommandArgumentValue.AddArgument("n", "no-backup",
            ["Skip creating a backup before the update"], false, 0);

        _forceArg = CommandArgumentValue.AddArgument("f", "force",
            ["Continue even if there are conflicts"], false, 0);
    }

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantArg);
        var versionString = CommandArgumentValue.GetArgumentScalarValue<string>(_versionArg);
        var modeString = CommandArgumentValue.IsArgumentUsed(_modeArg)
            ? CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_modeArg)
            : "Merge";

        var dryRun = CommandArgumentValue.IsArgumentUsed(_dryRunArg);
        var noBackup = CommandArgumentValue.IsArgumentUsed(_noBackupArg);
        var force = CommandArgumentValue.IsArgumentUsed(_forceArg);

        var targetVersion = new BlueprintId(versionString);

        if (!Enum.TryParse<BlueprintUpdateMode>(modeString, true, out var updateMode))
        {
            Logger.LogError("Invalid update mode: {Mode}. Valid modes are: Safe, Merge, Full, Migration", modeString);
            return;
        }

        if (dryRun)
        {
            Logger.LogInformation("DRY RUN: No changes will be made");
            Logger.LogInformation("");
        }

        Logger.LogInformation("Updating tenant {TenantId} to {TargetVersion} using {Mode} mode",
            tenantId, targetVersion.FullName, updateMode);
        Logger.LogInformation("");

        var options = new BlueprintUpdateOptions
        {
            DryRun = dryRun,
            CreateBackup = !noBackup,
            ContinueOnError = force
        };

        var result = await _blueprintService.ApplyUpdateAsync(tenantId, targetVersion, updateMode, options);

        if (result.Success)
        {
            Logger.LogInformation("Update completed successfully!");
            Logger.LogInformation("");
            Logger.LogInformation("Summary:");
            Logger.LogInformation("  Entities added:   {Count}", result.EntitiesAdded);
            Logger.LogInformation("  Entities updated: {Count}", result.EntitiesUpdated);
            Logger.LogInformation("  Entities deleted: {Count}", result.EntitiesDeleted);
            Logger.LogInformation("  Entities skipped: {Count}", result.EntitiesSkipped);

            if (!string.IsNullOrEmpty(result.BackupId))
            {
                Logger.LogInformation("");
                Logger.LogInformation("Backup created: {BackupId}", result.BackupId);
            }

            if (result.NewBlueprintInfo != null)
            {
                Logger.LogInformation("");
                Logger.LogInformation("New Blueprint Version: {Version}", result.NewBlueprintInfo.BlueprintId.FullName);
            }
        }
        else
        {
            Logger.LogError("Update failed!");
            Logger.LogInformation("");

            if (result.Errors.Count > 0)
            {
                Logger.LogError("Errors:");
                foreach (var error in result.Errors)
                {
                    Logger.LogError("  - {Error}", error);
                }
            }
        }

        if (result.Warnings.Count > 0)
        {
            Logger.LogInformation("");
            Logger.LogWarning("Warnings:");
            foreach (var warning in result.Warnings)
            {
                Logger.LogWarning("  - {Warning}", warning);
            }
        }
    }
}
