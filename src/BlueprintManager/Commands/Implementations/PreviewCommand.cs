using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.BlueprintManager.Commands.Implementations;

/// <summary>
/// Command to preview changes that would be made by a blueprint update.
/// </summary>
internal class PreviewCommand : Command<BpmToolOptions>
{
    private readonly IBlueprintService _blueprintService;
    private readonly IArgument _tenantArg;
    private readonly IArgument _versionArg;
    private readonly IArgument _modeArg;

    public PreviewCommand(
        ILogger<PreviewCommand> logger,
        IOptions<BpmToolOptions> options,
        IBlueprintService blueprintService)
        : base(logger, "preview", "Previews changes that would be made by a blueprint update", options)
    {
        _blueprintService = blueprintService;

        _tenantArg = CommandArgumentValue.AddArgument("t", "tenant",
            ["Tenant identifier"], true, 1);

        _versionArg = CommandArgumentValue.AddArgument("b", "blueprint",
            ["Target blueprint version (e.g., 'MyBlueprint-2.0.0')"], true, 1);

        _modeArg = CommandArgumentValue.AddArgument("m", "mode",
            ["Update mode: Safe, Merge, Full, or Migration (default: Merge)"], false, 1);
    }

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantArg);
        var versionString = CommandArgumentValue.GetArgumentScalarValue<string>(_versionArg);
        var modeString = CommandArgumentValue.IsArgumentUsed(_modeArg)
            ? CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_modeArg)
            : "Merge";

        var targetVersion = new BlueprintId(versionString);

        if (!Enum.TryParse<BlueprintUpdateMode>(modeString, true, out var updateMode))
        {
            Logger.LogError("Invalid update mode: {Mode}. Valid modes are: Safe, Merge, Full, Migration", modeString);
            return;
        }

        Logger.LogInformation("Preview: Update tenant {TenantId} to {TargetVersion} using {Mode} mode",
            tenantId, targetVersion.FullName, updateMode);
        Logger.LogInformation("");

        var preview = await _blueprintService.PreviewUpdateAsync(tenantId, targetVersion, updateMode);

        Logger.LogInformation("Summary:");
        Logger.LogInformation("  Entities to add:    {Count}", preview.EntitiesToAdd);
        Logger.LogInformation("  Entities to update: {Count}", preview.EntitiesToUpdate);
        Logger.LogInformation("  Entities to delete: {Count}", preview.EntitiesToDelete);
        Logger.LogInformation("");

        if (preview.Conflicts.Count > 0)
        {
            Logger.LogWarning("Conflicts ({Count}):", preview.Conflicts.Count);
            foreach (var conflict in preview.Conflicts)
            {
                Logger.LogWarning("  [{Type}] {EntityId}: {Description}",
                    conflict.ConflictType, conflict.EntityId, conflict.Description);
                Logger.LogWarning("    Suggested resolution: {Resolution}", conflict.SuggestedResolution);
            }
            Logger.LogInformation("");
        }

        if (preview.Warnings.Count > 0)
        {
            Logger.LogWarning("Warnings:");
            foreach (var warning in preview.Warnings)
            {
                Logger.LogWarning("  - {Warning}", warning);
            }
            Logger.LogInformation("");
        }

        if (preview.CanProceed)
        {
            Logger.LogInformation("Update can proceed without manual intervention");
            Logger.LogInformation("");
            Logger.LogInformation("To apply this update, run:");
            Logger.LogInformation("  octo-bpm update -t {TenantId} -b {Version} -m {Mode}",
                tenantId, targetVersion.FullName, updateMode);
        }
        else
        {
            Logger.LogError("Update blocked by {Count} conflict(s). Resolve conflicts before proceeding",
                preview.Conflicts.Count);
        }
    }
}
