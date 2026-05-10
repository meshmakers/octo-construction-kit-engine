using System.Collections.Generic;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Read-only snapshot of the parts of a <c>CkArchive</c> entity the lifecycle service needs.
/// Intentionally narrower than the full RtEntity: the lifecycle decisions only depend on
/// <see cref="Status"/> plus identification fields. Backend-specific stores translate from their
/// concrete representation to this record. <c>Columns</c> carries the user-configured archive
/// columns (path/indexed/required) the data-store provider needs to generate DDL — empty when the
/// archive only has the standard time-series columns.
/// </summary>
public sealed record CkArchiveSnapshot(
    OctoObjectId RtId,
    RtCkId<CkTypeId> TargetCkTypeId,
    CkArchiveStatus Status,
    string? RtWellKnownName,
    IReadOnlyList<CkArchiveColumnSpec> Columns);

/// <summary>
/// Minimal projection of a <c>CkArchive.columns[]</c> entry — just enough for the data-store
/// provider to resolve types via the CK model and generate DDL. The CK record carries more fields
/// than this in some models, but the lifecycle service only cares about path / indexed / required.
/// </summary>
public sealed record CkArchiveColumnSpec(
    string Path,
    bool Indexed,
    bool Required);
