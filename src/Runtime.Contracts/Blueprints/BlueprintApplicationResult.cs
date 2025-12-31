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
    /// List of CK models that were loaded
    /// </summary>
    public List<CkModelId> LoadedCkModels { get; private init; }

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
    /// Creates a successful result
    /// </summary>
    /// <param name="tenantId">The tenant id</param>
    /// <param name="blueprintId">The blueprint id</param>
    /// <param name="loadedCkModels">List of loaded CK models</param>
    /// <param name="appliedSeedDataFiles">List of applied seed data files</param>
    /// <param name="entitiesCreated">Number of entities created</param>
    /// <param name="operationResult">Operation result with messages</param>
    /// <returns>A successful result</returns>
    public static BlueprintApplicationResult Success(
        string tenantId,
        BlueprintId blueprintId,
        List<CkModelId> loadedCkModels,
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
}
