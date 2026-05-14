namespace Meshmakers.Octo.Runtime.Contracts.Blueprints;

/// <summary>
/// Defines how a blueprint was applied to a tenant
/// </summary>
public enum BlueprintApplicationMode
{
    /// <summary>
    /// Initial application of a blueprint to a new tenant
    /// </summary>
    Initial,

    /// <summary>
    /// Update from a previous version of the same blueprint
    /// </summary>
    Update,

    /// <summary>
    /// Update using an explicit migration script
    /// </summary>
    Migration,

    /// <summary>
    /// Re-apply of an already-installed blueprint version
    /// (triggered via --force after storage corruption or manual cleanup).
    /// Seed data is re-imported using upsert semantics.
    /// </summary>
    ReApply
}
