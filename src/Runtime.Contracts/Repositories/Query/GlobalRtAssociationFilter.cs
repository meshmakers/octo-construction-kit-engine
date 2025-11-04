namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
/// Global filter settings during runtime association queries
/// </summary>
public class GlobalRtAssociationFilter(bool includeArchived)
{
    /// <summary>
    /// Returns true when also archived associations should be returned by the query.
    /// </summary>
    public bool IncludeArchived { get; } = includeArchived;
}