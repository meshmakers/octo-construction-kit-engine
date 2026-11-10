namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
/// Defines what <see cref="RtPathEvaluator.SetValue(Meshmakers.Octo.ConstructionKit.Contracts.Services.ICkCacheService, string, RepositoryEntities.RtTypeWithAttributes, string, object?, RtPathNullHandling)" />
/// does with a <c>null</c> value whose path runs through a record that does not exist yet
/// (for example <c>Amount.Value</c> while <c>Amount</c> is not set).
/// </summary>
public enum RtPathNullHandling
{
    /// <summary>
    /// The null is dropped: the missing record is not created and the attribute stays missing.
    /// This is the behaviour of the overloads without this parameter (query-row creates and updates
    /// must not create empty records for empty cells, AB#1988).
    /// </summary>
    KeepMissingRecords = 0,

    /// <summary>
    /// The null is an explicit value: missing records along the path are created (the same way a
    /// non-null value creates them) and the addressed member is set to an explicit <c>null</c>.
    /// Used where an explicit null has to stay apart from a missing value, for example pipeline
    /// updates that write to an archive, where only an explicit null clears a column (AB#8622, AB#8625).
    /// </summary>
    CreateMissingRecords = 1
}
