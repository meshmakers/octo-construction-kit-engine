using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

namespace Meshmakers.Octo.Runtime.Contracts.Blueprints;

/// <summary>
/// Result of validating a blueprint
/// </summary>
public class BlueprintValidationResult
{
    /// <summary>
    /// Creates a new instance of <see cref="BlueprintValidationResult"/>
    /// </summary>
    private BlueprintValidationResult()
    {
        MissingCkModels = [];
        MissingBlueprints = [];
        MissingSeedDataFiles = [];
        OperationResult = new OperationResult();
    }

    /// <summary>
    /// Indicates if the blueprint is valid and can be applied
    /// </summary>
    public bool IsValid { get; private init; }

    /// <summary>
    /// The blueprint that was validated
    /// </summary>
    public BlueprintId? BlueprintId { get; private init; }

    /// <summary>
    /// List of CK models that are required but not available
    /// </summary>
    public List<CkModelIdVersionRange> MissingCkModels { get; private init; }

    /// <summary>
    /// List of composed blueprints that are required but not available
    /// </summary>
    public List<BlueprintIdVersionRange> MissingBlueprints { get; private init; }

    /// <summary>
    /// List of seed data files that are referenced but not found
    /// </summary>
    public List<string> MissingSeedDataFiles { get; private init; }

    /// <summary>
    /// Operation result containing validation messages
    /// </summary>
    public OperationResult OperationResult { get; private init; }

    /// <summary>
    /// Creates a valid result
    /// </summary>
    /// <param name="blueprintId">The blueprint id</param>
    /// <param name="operationResult">Operation result with messages</param>
    /// <returns>A valid result</returns>
    public static BlueprintValidationResult Valid(BlueprintId blueprintId, OperationResult operationResult)
    {
        return new BlueprintValidationResult
        {
            IsValid = true,
            BlueprintId = blueprintId,
            OperationResult = operationResult
        };
    }

    /// <summary>
    /// Creates an invalid result
    /// </summary>
    /// <param name="blueprintId">The blueprint id</param>
    /// <param name="missingCkModels">List of missing CK models</param>
    /// <param name="missingBlueprints">List of missing blueprints</param>
    /// <param name="missingSeedDataFiles">List of missing seed data files</param>
    /// <param name="operationResult">Operation result with error messages</param>
    /// <returns>An invalid result</returns>
    public static BlueprintValidationResult Invalid(
        BlueprintId blueprintId,
        List<CkModelIdVersionRange> missingCkModels,
        List<BlueprintIdVersionRange> missingBlueprints,
        List<string> missingSeedDataFiles,
        OperationResult operationResult)
    {
        return new BlueprintValidationResult
        {
            IsValid = false,
            BlueprintId = blueprintId,
            MissingCkModels = missingCkModels,
            MissingBlueprints = missingBlueprints,
            MissingSeedDataFiles = missingSeedDataFiles,
            OperationResult = operationResult
        };
    }
}
