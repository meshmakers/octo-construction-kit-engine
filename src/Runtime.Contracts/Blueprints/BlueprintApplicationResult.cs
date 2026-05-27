using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

namespace Meshmakers.Octo.Runtime.Contracts.Blueprints;

/// <summary>
/// Result of applying a blueprint to a tenant
/// </summary>
public class BlueprintApplicationResult
{
    /// <summary>
    /// Creates a new instance of <see cref="BlueprintApplicationResult"/>
    /// </summary>
    private BlueprintApplicationResult()
    {
        LoadedCkModels = [];
        AppliedSeedDataFiles = [];
        OperationResult = new OperationResult();
    }

    /// <summary>
    /// Indicates if the blueprint was applied successfully
    /// </summary>
    public bool IsSuccess { get; private init; }

    /// <summary>
    /// The blueprint that was applied
    /// </summary>
    public BlueprintId? BlueprintId { get; private init; }

    /// <summary>
    /// The tenant that the blueprint was applied to
    /// </summary>
    public string? TenantId { get; private init; }

    /// <summary>
    /// List of CK models that were loaded (as version ranges)
    /// </summary>
    public List<CkModelIdVersionRange> LoadedCkModels { get; private init; }

    /// <summary>
    /// List of seed data files that were applied
    /// </summary>
    public List<string> AppliedSeedDataFiles { get; private init; }

    /// <summary>
    /// Number of entities created from seed data
    /// </summary>
    public int EntitiesCreated { get; private init; }

    /// <summary>
    /// Operation result containing messages and errors
    /// </summary>
    public OperationResult OperationResult { get; private init; }

    /// <summary>
    /// True when the blueprint's <c>requires:</c> preconditions did not match the tenant's
    /// variable context, so the apply was a no-op (no seed data was imported, no
    /// installation row was recorded). <see cref="IsSuccess"/> is still <c>true</c> in this
    /// case — the apply did not fail, it just had nothing to do.
    /// </summary>
    public bool WasSkipped { get; private init; }

    /// <summary>
    /// Human-readable reason why the apply was skipped, populated when
    /// <see cref="WasSkipped"/> is <c>true</c>.
    /// </summary>
    public string? SkipReason { get; private init; }

    /// <summary>
    /// Creates a successful result
    /// </summary>
    /// <param name="tenantId">The tenant id</param>
    /// <param name="blueprintId">The blueprint id</param>
    /// <param name="loadedCkModels">List of loaded CK models (as version ranges)</param>
    /// <param name="appliedSeedDataFiles">List of applied seed data files</param>
    /// <param name="entitiesCreated">Number of entities created</param>
    /// <param name="operationResult">Operation result with messages</param>
    /// <returns>A successful result</returns>
    public static BlueprintApplicationResult Success(
        string tenantId,
        BlueprintId blueprintId,
        List<CkModelIdVersionRange> loadedCkModels,
        List<string> appliedSeedDataFiles,
        int entitiesCreated,
        OperationResult operationResult)
    {
        return new BlueprintApplicationResult
        {
            IsSuccess = true,
            TenantId = tenantId,
            BlueprintId = blueprintId,
            LoadedCkModels = loadedCkModels,
            AppliedSeedDataFiles = appliedSeedDataFiles,
            EntitiesCreated = entitiesCreated,
            OperationResult = operationResult
        };
    }

    /// <summary>
    /// Creates a failed result
    /// </summary>
    /// <param name="operationResult">Operation result with error messages</param>
    /// <returns>A failed result</returns>
    public static BlueprintApplicationResult Failed(OperationResult operationResult)
    {
        return new BlueprintApplicationResult
        {
            IsSuccess = false,
            OperationResult = operationResult
        };
    }

    /// <summary>
    /// Creates a successful no-op result indicating the blueprint was skipped because its
    /// <c>requires:</c> preconditions did not match the tenant's variable context. Callers
    /// that loop over multiple blueprints can rely on <see cref="WasSkipped"/> to
    /// distinguish "applied" from "skipped" without re-reading the manifest.
    /// </summary>
    /// <param name="tenantId">Target tenant identifier.</param>
    /// <param name="blueprintId">Blueprint that was evaluated.</param>
    /// <param name="reason">Human-readable reason why the apply was skipped.</param>
    /// <param name="operationResult">Operation result with informational messages.</param>
    /// <returns>A skipped result with <see cref="IsSuccess"/>=<c>true</c>.</returns>
    public static BlueprintApplicationResult Skipped(
        string tenantId,
        BlueprintId blueprintId,
        string reason,
        OperationResult operationResult)
    {
        return new BlueprintApplicationResult
        {
            IsSuccess = true,
            WasSkipped = true,
            SkipReason = reason,
            TenantId = tenantId,
            BlueprintId = blueprintId,
            OperationResult = operationResult
        };
    }
}
