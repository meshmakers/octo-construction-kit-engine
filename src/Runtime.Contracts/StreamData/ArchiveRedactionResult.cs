using System.Collections.Generic;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Result of <see cref="IArchiveLifecycleService.RedactHiddenColumnsAsync"/> (AB#6247): the archive, its status after the
/// call and the ingested column paths whose columns and stored values were dropped (empty when nothing reached Hidden).
/// </summary>
/// <param name="ArchiveRtId">The archive.</param>
/// <param name="Status">The status after the call: Activated for an archive that was Failed on Hidden columns.</param>
/// <param name="RemovedColumns">The dropped column paths.</param>
public sealed record ArchiveRedactionResult(
    OctoObjectId ArchiveRtId,
    CkArchiveStatus Status,
    IReadOnlyList<string> RemovedColumns);
