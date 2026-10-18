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
    /// True when the apply was refused because the CK models it would install or raise break an installed
    /// model (tenant upgrade guard, AB#6871). Nothing was changed. <see cref="CkUpgradeBlockers"/> names what
    /// would break; <see cref="IsSuccess"/> is <c>false</c>.
    /// </summary>
    public bool IsCkUpgradeBlocked { get; private init; }

    /// <summary>
    /// True when the tenant upgrade guard itself could not judge the CK update set. The apply failed closed and
    /// nothing was changed.
    /// </summary>
    public bool CkUpgradeGuardFailed { get; private init; }

    /// <summary>
    /// The installed models the CK update set breaks. With <see cref="IsCkUpgradeBlocked"/> they are the reason of
    /// the refusal; on a successful result they are the blockers the caller forced past
    /// (<see cref="BlueprintCkUpgradeGuardMode.ForceBreakingUpdate"/>). Empty otherwise.
    /// </summary>
    public IReadOnlyList<BlueprintCkUpgradeBlocker> CkUpgradeBlockers { get; private init; } = [];

    /// <summary>
    /// The CK models (full ids) that were judged as one set; empty when the guard did not run.
    /// </summary>
    public IReadOnlyList<string> CkUpdateSet { get; private init; } = [];

    /// <summary>
    /// Creates a successful result (six-parameter form kept for consumers compiled against earlier packages)
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
        return Success(tenantId, blueprintId, loadedCkModels, appliedSeedDataFiles, entitiesCreated,
            operationResult, null, null);
    }

    /// <summary>
    /// Creates a successful result
    /// </summary>
    /// <param name="tenantId">The tenant id</param>
    /// <param name="blueprintId">The blueprint id</param>
    /// <param name="loadedCkModels">List of loaded CK models (as version ranges)</param>
    /// <param name="appliedSeedDataFiles">List of applied seed data files</param>
    /// <param name="entitiesCreated">Number of entities created</param>
    /// <param name="operationResult">Operation result with messages</param>
    /// <param name="forcedCkUpgradeBlockers">The blockers the caller forced past (AB#6871); none by default</param>
    /// <param name="ckUpdateSet">The CK models (full ids) the tenant upgrade guard judged as one set; none by default</param>
    /// <returns>A successful result</returns>
    public static BlueprintApplicationResult Success(
        string tenantId,
        BlueprintId blueprintId,
        List<CkModelIdVersionRange> loadedCkModels,
        List<string> appliedSeedDataFiles,
        int entitiesCreated,
        OperationResult operationResult,
        IReadOnlyList<BlueprintCkUpgradeBlocker>? forcedCkUpgradeBlockers,
        IReadOnlyList<string>? ckUpdateSet)
    {
        return new BlueprintApplicationResult
        {
            IsSuccess = true,
            TenantId = tenantId,
            BlueprintId = blueprintId,
            LoadedCkModels = loadedCkModels,
            AppliedSeedDataFiles = appliedSeedDataFiles,
            EntitiesCreated = entitiesCreated,
            OperationResult = operationResult,
            CkUpgradeBlockers = forcedCkUpgradeBlockers ?? [],
            CkUpdateSet = ckUpdateSet ?? []
        };
    }

    /// <summary>
    /// Creates the result of an apply that the tenant upgrade guard refused (AB#6871): nothing was changed.
    /// </summary>
    /// <param name="tenantId">The tenant id</param>
    /// <param name="blueprintId">The blueprint id</param>
    /// <param name="ckUpdateSet">The CK models (full ids) judged as one set</param>
    /// <param name="blockers">The installed models the set would break</param>
    /// <param name="operationResult">Operation result with the error message</param>
    /// <returns>A failed result with <see cref="IsCkUpgradeBlocked"/> = <c>true</c></returns>
    public static BlueprintApplicationResult CkUpgradeBlocked(
        string tenantId,
        BlueprintId blueprintId,
        IReadOnlyList<string> ckUpdateSet,
        IReadOnlyList<BlueprintCkUpgradeBlocker> blockers,
        OperationResult operationResult)
    {
        return new BlueprintApplicationResult
        {
            IsSuccess = false,
            IsCkUpgradeBlocked = true,
            TenantId = tenantId,
            BlueprintId = blueprintId,
            CkUpdateSet = ckUpdateSet,
            CkUpgradeBlockers = blockers,
            OperationResult = operationResult
        };
    }

    /// <summary>
    /// Creates the result of an apply that failed closed because the tenant upgrade guard could not judge
    /// the CK update set (AB#6871): nothing was changed.
    /// </summary>
    /// <param name="operationResult">Operation result with the error message</param>
    /// <returns>A failed result with <see cref="CkUpgradeGuardFailed"/> = <c>true</c></returns>
    public static BlueprintApplicationResult CkUpgradeGuardFailure(OperationResult operationResult)
    {
        return new BlueprintApplicationResult
        {
            IsSuccess = false,
            CkUpgradeGuardFailed = true,
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
