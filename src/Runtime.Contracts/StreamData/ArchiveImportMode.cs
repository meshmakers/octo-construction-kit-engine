namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Controls how <see cref="IStreamDataRepository.ImportRowsAsync"/> writes imported archive rows.
/// Archive data export/import concept (AB#4230) §7.
/// </summary>
public enum ArchiveImportMode
{
    /// <summary>
    /// Rows are inserted; a conflict on the natural key is a no-op update (the existing row's
    /// user-column values are preserved). Default for raw archives.
    /// </summary>
    InsertOnly = 0,

    /// <summary>
    /// Rows are upserted: a conflict on the natural key overwrites the existing user-column values
    /// with the imported ones (the same <c>ON CONFLICT DO UPDATE</c> path used by the windowed
    /// insert). Required for windowed (rollup / time-range) archives.
    /// </summary>
    Upsert = 1,
}
