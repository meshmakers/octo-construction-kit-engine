namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
///     Controls how navigation properties affect the result set.
/// </summary>
public enum NavigationFilterMode
{
    /// <summary>
    ///     Entities without matching associations are filtered out (pre-pagination).
    ///     This is the default behavior.
    /// </summary>
    Filter = 0,

    /// <summary>
    ///     Entities without matching associations are kept with null values (post-pagination).
    ///     Navigation lookups run only on the paginated subset, improving performance.
    /// </summary>
    Include = 1
}
