using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;

namespace Meshmakers.Octo.Runtime.Contracts.Blueprints;

/// <summary>
/// Executes blueprint migration scripts
/// </summary>
public interface IBlueprintMigrationExecutor
{
    /// <summary>
    /// Executes a migration script
    /// </summary>
    /// <param name="tenantId">Target tenant identifier</param>
    /// <param name="migration">The migration script to execute</param>
    /// <param name="options">Migration options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the migration execution</returns>
    Task<BlueprintMigrationExecutionResult> ExecuteAsync(
        string tenantId,
        BlueprintMigrationDto migration,
        BlueprintMigrationExecutionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a migration script without executing it
    /// </summary>
    /// <param name="tenantId">Target tenant identifier</param>
    /// <param name="migration">The migration script to validate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result with any issues found</returns>
    Task<BlueprintMigrationValidationResult> ValidateAsync(
        string tenantId,
        BlueprintMigrationDto migration,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for migration execution
/// </summary>
public class BlueprintMigrationExecutionOptions
{
    /// <summary>
    /// If true, only simulate the migration without making changes
    /// </summary>
    public bool DryRun { get; set; } = false;

    /// <summary>
    /// If true, continue executing steps even if some fail
    /// </summary>
    public bool ContinueOnError { get; set; } = false;

    /// <summary>
    /// The blueprint ID that initiated this migration (for tagging entities)
    /// </summary>
    public string? BlueprintSource { get; set; }
}

/// <summary>
/// Result of a migration execution
/// </summary>
public class BlueprintMigrationExecutionResult
{
    /// <summary>
    /// Whether the migration completed successfully
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Total number of steps in the migration
    /// </summary>
    public int TotalSteps { get; set; }

    /// <summary>
    /// Number of steps that completed successfully
    /// </summary>
    public int CompletedSteps { get; set; }

    /// <summary>
    /// Number of steps that were skipped
    /// </summary>
    public int SkippedSteps { get; set; }

    /// <summary>
    /// Number of steps that failed
    /// </summary>
    public int FailedSteps { get; set; }

    /// <summary>
    /// Number of entities added
    /// </summary>
    public int EntitiesAdded { get; set; }

    /// <summary>
    /// Number of entities updated
    /// </summary>
    public int EntitiesUpdated { get; set; }

    /// <summary>
    /// Number of entities deleted
    /// </summary>
    public int EntitiesDeleted { get; set; }

    /// <summary>
    /// Detailed results for each step
    /// </summary>
    public List<BlueprintMigrationStepResult> StepResults { get; set; } = [];

    /// <summary>
    /// Errors that occurred during execution
    /// </summary>
    public List<string> Errors { get; set; } = [];

    /// <summary>
    /// Warnings generated during execution
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Result of a single migration step
/// </summary>
public class BlueprintMigrationStepResult
{
    /// <summary>
    /// The step ID from the migration script
    /// </summary>
    public required string StepId { get; set; }

    /// <summary>
    /// Whether the step completed successfully
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Whether the step was skipped
    /// </summary>
    public bool Skipped { get; set; }

    /// <summary>
    /// Reason for skipping (if skipped)
    /// </summary>
    public string? SkipReason { get; set; }

    /// <summary>
    /// Error message if the step failed
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Number of entities affected by this step
    /// </summary>
    public int EntitiesAffected { get; set; }
}

/// <summary>
/// Result of migration validation
/// </summary>
public class BlueprintMigrationValidationResult
{
    /// <summary>
    /// Whether the migration is valid and can be executed
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Validation errors that would prevent execution
    /// </summary>
    public List<BlueprintMigrationValidationIssue> Errors { get; set; } = [];

    /// <summary>
    /// Validation warnings that don't prevent execution
    /// </summary>
    public List<BlueprintMigrationValidationIssue> Warnings { get; set; } = [];
}

/// <summary>
/// A validation issue found in the migration script
/// </summary>
public class BlueprintMigrationValidationIssue
{
    /// <summary>
    /// The step ID where the issue was found (null for global issues)
    /// </summary>
    public string? StepId { get; set; }

    /// <summary>
    /// Description of the issue
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// The property or element that has the issue
    /// </summary>
    public string? PropertyPath { get; set; }
}
