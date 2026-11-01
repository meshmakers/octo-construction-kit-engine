using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// Adding an ingested column to an activated archive (AB#7997).
/// </summary>
public sealed partial class ArchiveLifecycleService
{
    /// <inheritdoc />
    public async Task<bool> AddArchiveColumnAsync(
        OctoObjectId archiveRtId, string path, bool indexed, CancellationToken cancellationToken = default)
    {
        var trimmed = path?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new ArchivePathInvalidException(archiveRtId, trimmed, "the path of a column must not be empty.");
        }

        var snapshot = await LoadAsync(archiveRtId);
        if (snapshot.Status != CkArchiveStatus.Activated)
        {
            throw new InvalidArchiveStateTransitionException(archiveRtId, snapshot.Status, "add a column to");
        }

        if (snapshot.RollupAggregations is not null)
        {
            throw new ArchivePathInvalidException(archiveRtId, trimmed,
                "the columns of a rollup archive are derived from its aggregations; change the aggregations instead.");
        }

        var physicalName = RollupColumnGenerator.SanitisePath(trimmed);
        if (snapshot.Columns.Any(c => !c.IsComputed && !string.IsNullOrWhiteSpace(c.Path)
                                      && string.Equals(RollupColumnGenerator.SanitisePath(c.Path), physicalName,
                                          StringComparison.Ordinal)))
        {
            // Idempotent: a re-run of the command, or a seed applied twice, finds the column in place.
            return false;
        }

        if (snapshot.Columns.Any(c => c.IsComputed && c.Name is { } name
                                      && string.Equals(RollupColumnGenerator.SanitisePath(name), physicalName,
                                          StringComparison.Ordinal)))
        {
            throw new ArchivePathInvalidException(archiveRtId, trimmed,
                $"the archive already has a computed column that uses the name '{physicalName}'.");
        }

        var column = new CkArchiveColumnSpec(trimmed, indexed, Required: false);
        var prospective = snapshot with { Columns = snapshot.Columns.Append(column).ToList() };
        EnsureNoHiddenColumns(prospective);

        using var activity = StreamDataDiagnostics.ActivitySource.StartActivity("archive.addColumn");
        activity?.SetTag("streamdata.archive.rtid", archiveRtId.ToString());
        activity?.SetTag("streamdata.column.path", trimmed);

        // Storage first, definition last (concept §11): a failed ALTER leaves the definition untouched, so the archive
        // never declares a column its table lacks, and the call can simply be repeated.
        await _repository.AddIngestedColumnStorageAsync(prospective, trimmed, cancellationToken);
        await _store.AddIngestedColumnAsync(archiveRtId, column);

        _logger.LogInformation(
            "Column '{Path}' added to activated archive {Archive}; existing rows read NULL for it, new rows fill it.",
            trimmed, archiveRtId);
        return true;
    }
}
