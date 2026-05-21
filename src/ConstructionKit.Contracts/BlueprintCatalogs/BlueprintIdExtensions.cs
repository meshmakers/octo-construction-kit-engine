namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

/// <summary>
/// Extension helpers for <see cref="BlueprintId" />.
/// </summary>
public static class BlueprintIdExtensions
{
    /// <summary>
    /// OctoMesh convention: blueprint names that start with <c>System.</c> are owned and
    /// auto-managed by an OctoMesh service (e.g. <c>System.Communication-1.0.0</c> is applied by
    /// the Communication Controller on tenant enable / startup). User-installable blueprints use
    /// any other naming prefix.
    /// </summary>
    /// <remarks>
    /// The check is on the blueprint *name*, not on the catalog. A service-managed blueprint
    /// remains service-managed even if a copy of it shows up in a non-embedded catalog — the
    /// Studio uses this flag to hide install / uninstall actions consistently.
    /// </remarks>
    public const string ServiceManagedNamePrefix = "System.";

    /// <summary>
    /// Returns <c>true</c> when this blueprint is service-managed by OctoMesh — see
    /// <see cref="ServiceManagedNamePrefix" />.
    /// </summary>
    public static bool IsServiceManaged(this BlueprintId blueprintId)
    {
        return blueprintId.Name.StartsWith(ServiceManagedNamePrefix, StringComparison.Ordinal);
    }
}
