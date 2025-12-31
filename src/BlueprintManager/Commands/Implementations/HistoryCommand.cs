using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.BlueprintManager.Commands.Implementations;

/// <summary>
/// Command to show the blueprint application history for a tenant.
/// </summary>
internal class HistoryCommand : Command<BpmToolOptions>
{
    private readonly IBlueprintService _blueprintService;
    private readonly IArgument _tenantArg;
    private readonly IArgument _limitArg;

    public HistoryCommand(
        ILogger<HistoryCommand> logger,
        IOptions<BpmToolOptions> options,
        IBlueprintService blueprintService)
        : base(logger, "history", "Shows the blueprint application history for a tenant", options)
    {
        _blueprintService = blueprintService;

        _tenantArg = CommandArgumentValue.AddArgument("t", "tenant",
            ["Tenant identifier"], true, 1);

        _limitArg = CommandArgumentValue.AddArgument("l", "limit",
            ["Maximum number of entries to show (default: 10)"], false, 1);
    }

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantArg);
        var limit = CommandArgumentValue.IsArgumentUsed(_limitArg)
            ? CommandArgumentValue.GetArgumentScalarValueOrDefault<int>(_limitArg)
            : 10;

        Logger.LogInformation("Blueprint history for tenant: {TenantId}", tenantId);
        Logger.LogInformation("");

        var history = await _blueprintService.GetHistoryAsync(tenantId);

        if (history.Count == 0)
        {
            Logger.LogInformation("No blueprint history found for this tenant");
            return;
        }

        Logger.LogInformation("Found {Count} entries (showing last {Limit}):", history.Count, Math.Min(limit, history.Count));
        Logger.LogInformation("");

        // Show entries in reverse chronological order (newest first)
        var entries = history.Reverse().Take(limit).ToList();
        var index = 0;

        foreach (var entry in entries)
        {
            var isCurrent = index == 0 ? " (current)" : "";
            Logger.LogInformation("{Index}. {BlueprintId}{Current}",
                index + 1, entry.BlueprintId.FullName, isCurrent);
            Logger.LogInformation("   Applied:  {AppliedAt:yyyy-MM-dd HH:mm:ss UTC}", entry.AppliedAt);
            Logger.LogInformation("   Mode:     {Mode}", entry.ApplicationMode);

            if (entry.PreviousVersion != null)
            {
                Logger.LogInformation("   From:     {PreviousVersion}", entry.PreviousVersion.FullName);
            }

            Logger.LogInformation("   Changes:  {Created} created, {Updated} updated, {Deleted} deleted",
                entry.EntitiesCreated, entry.EntitiesUpdated, entry.EntitiesDeleted);

            if (!string.IsNullOrEmpty(entry.AppliedBy))
            {
                Logger.LogInformation("   By:       {AppliedBy}", entry.AppliedBy);
            }

            if (!string.IsNullOrEmpty(entry.Notes))
            {
                Logger.LogInformation("   Notes:    {Notes}", entry.Notes);
            }

            Logger.LogInformation("");
            index++;
        }

        if (history.Count > limit)
        {
            Logger.LogInformation("... and {Count} more entries. Use -l to show more", history.Count - limit);
        }
    }
}
