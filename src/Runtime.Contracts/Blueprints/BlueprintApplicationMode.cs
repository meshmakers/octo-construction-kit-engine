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
    Migration
}
