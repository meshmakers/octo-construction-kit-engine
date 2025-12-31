namespace Meshmakers.Octo.Runtime.Contracts.Blueprints;

/// <summary>
/// Defines how a blueprint update should be applied
/// </summary>
public enum BlueprintUpdateMode
{
    /// <summary>
    /// Only add new entities, never modify or delete existing ones.
    /// Safest option - no data loss possible.
    /// </summary>
    Safe,

    /// <summary>
    /// Add new entities and update blueprint-managed entities (rtBlueprintLocked=true).
    /// User modifications to unlocked entities are preserved.
    /// </summary>
    Merge,

    /// <summary>
    /// Full update: add, update, and delete according to the new blueprint.
    /// User modifications to blueprint entities may be lost.
    /// </summary>
    Full,

    /// <summary>
    /// Use an explicit migration script from the blueprint.
    /// Provides fine-grained control over the update process.
    /// </summary>
    Migration
}
