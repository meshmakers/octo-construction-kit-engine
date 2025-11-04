namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
/// Global filter settings during runtime entity queries
/// </summary>
public class GlobalRtEntityFilter(bool includeArchived)
{
    /// <summary>
    /// Returns true when also archived entities should be returned by the query.
    /// </summary>
    public bool IncludeArchived { get; } = includeArchived;
}