using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
/// Executes blueprint migration scripts
/// </summary>
internal class MigrationExecutor : IMigrationExecutor
{
    private readonly ILogger<MigrationExecutor> _logger;

    /// <summary>
    /// Creates a new instance of <see cref="MigrationExecutor"/>
    /// </summary>
    public MigrationExecutor(ILogger<MigrationExecutor> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MigrationExecutionResult> ExecuteAsync(
        string tenantId,
        BlueprintMigrationDto migration,
        MigrationExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new MigrationExecutionOptions();

        _logger.LogInformation(
            "Executing migration from {SourceVersion} to {TargetVersion} for tenant {TenantId} (DryRun: {DryRun})",
            migration.SourceVersion,
            migration.TargetVersion,
            tenantId,
            options.DryRun);

        var result = new MigrationExecutionResult
        {
            TotalSteps = migration.Steps.Count
        };

        // Validate preconditions
        if (migration.PreConditions != null)
        {
            foreach (var condition in migration.PreConditions)
            {
                var conditionMet = await EvaluateConditionAsync(tenantId, condition, cancellationToken)
                    .ConfigureAwait(false);

                if (!conditionMet)
                {
                    result.Success = false;
                    result.Errors.Add($"Precondition failed: {condition.Type}");
                    return result;
                }
            }
        }

        // Execute each step
        foreach (var step in migration.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stepResult = await ExecuteStepAsync(tenantId, step, options, cancellationToken)
                .ConfigureAwait(false);

            result.StepResults.Add(stepResult);

            if (stepResult.Success)
            {
                result.CompletedSteps++;
                result.EntitiesAdded += stepResult.EntitiesAffected;
            }
            else if (stepResult.Skipped)
            {
                result.SkippedSteps++;
            }
            else
            {
                result.FailedSteps++;
                result.Errors.Add($"Step '{step.StepId}' failed: {stepResult.Error}");

                if (!options.ContinueOnError && !step.ContinueOnError)
                {
                    _logger.LogError("Migration aborted due to step failure: {StepId}", step.StepId);
                    break;
                }
            }
        }

        // Run post-validations
        if (migration.PostValidations != null && result.FailedSteps == 0)
        {
            foreach (var validation in migration.PostValidations)
            {
                var validationResult = await RunValidationAsync(tenantId, validation, cancellationToken)
                    .ConfigureAwait(false);

                if (!validationResult.Success)
                {
                    if (validation.Severity == MigrationValidationSeverity.Error)
                    {
                        result.Errors.Add($"Post-validation '{validation.ValidationId}' failed: {validationResult.Message}");
                    }
                    else
                    {
                        result.Warnings.Add($"Post-validation '{validation.ValidationId}' warning: {validationResult.Message}");
                    }
                }
            }
        }

        result.Success = result.FailedSteps == 0 && !result.Errors.Any();

        _logger.LogInformation(
            "Migration completed: {Success}, {CompletedSteps}/{TotalSteps} steps completed, {SkippedSteps} skipped, {FailedSteps} failed",
            result.Success,
            result.CompletedSteps,
            result.TotalSteps,
            result.SkippedSteps,
            result.FailedSteps);

        return result;
    }

    /// <inheritdoc />
    public async Task<MigrationValidationResult> ValidateAsync(
        string tenantId,
        BlueprintMigrationDto migration,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Validating migration from {SourceVersion} to {TargetVersion}",
            migration.SourceVersion, migration.TargetVersion);

        var result = new MigrationValidationResult { IsValid = true };

        // Validate version format
        if (!IsValidVersion(migration.SourceVersion))
        {
            result.Errors.Add(new MigrationValidationIssue
            {
                Message = $"Invalid source version format: {migration.SourceVersion}",
                PropertyPath = "sourceVersion"
            });
        }

        if (!IsValidVersion(migration.TargetVersion))
        {
            result.Errors.Add(new MigrationValidationIssue
            {
                Message = $"Invalid target version format: {migration.TargetVersion}",
                PropertyPath = "targetVersion"
            });
        }

        // Validate each step
        foreach (var step in migration.Steps)
        {
            await ValidateStepAsync(step, result, cancellationToken).ConfigureAwait(false);
        }

        // Validate preconditions
        if (migration.PreConditions != null)
        {
            foreach (var condition in migration.PreConditions)
            {
                ValidateCondition(condition, result, "preConditions");
            }
        }

        // Validate post-validations
        if (migration.PostValidations != null)
        {
            foreach (var validation in migration.PostValidations)
            {
                if (string.IsNullOrEmpty(validation.ValidationId))
                {
                    result.Errors.Add(new MigrationValidationIssue
                    {
                        Message = "Validation ID is required",
                        PropertyPath = "postValidations"
                    });
                }
            }
        }

        result.IsValid = !result.Errors.Any();
        return result;
    }

    private async Task<MigrationStepResult> ExecuteStepAsync(
        string tenantId,
        MigrationStepDto step,
        MigrationExecutionOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Executing step {StepId}: {Action}", step.StepId, step.Action);

        var result = new MigrationStepResult { StepId = step.StepId };

        try
        {
            // Check step condition
            if (step.Condition != null)
            {
                var conditionMet = await EvaluateConditionAsync(tenantId, step.Condition, cancellationToken)
                    .ConfigureAwait(false);

                if (!conditionMet)
                {
                    result.Skipped = true;
                    result.SkipReason = "Condition not met";
                    return result;
                }
            }

            // Execute based on action type
            switch (step.Action)
            {
                case MigrationActionType.Add:
                    result.EntitiesAffected = await ExecuteAddAsync(tenantId, step, options, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                case MigrationActionType.Update:
                    result.EntitiesAffected = await ExecuteUpdateAsync(tenantId, step, options, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                case MigrationActionType.Delete:
                    result.EntitiesAffected = await ExecuteDeleteAsync(tenantId, step, options, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                case MigrationActionType.Rename:
                    result.EntitiesAffected = await ExecuteRenameAsync(tenantId, step, options, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                case MigrationActionType.Transform:
                    result.EntitiesAffected = await ExecuteTransformAsync(tenantId, step, options, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                default:
                    result.Error = $"Unknown action type: {step.Action}";
                    return result;
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing step {StepId}", step.StepId);
            result.Error = ex.Message;
        }

        return result;
    }

    private Task<int> ExecuteAddAsync(
        string tenantId,
        MigrationStepDto step,
        MigrationExecutionOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Adding entity for step {StepId}", step.StepId);

        // TODO: Implement actual entity creation using repository
        // This is a placeholder implementation
        // In a real implementation, this would:
        // 1. Parse the step.Data as entity data
        // 2. Set rtBlueprintSource to options.BlueprintSource
        // 3. Set rtBlueprintLocked to true
        // 4. Create the entity in the repository

        if (options.DryRun)
        {
            _logger.LogDebug("DryRun: Would add entity");
            return Task.FromResult(0);
        }

        return Task.FromResult(1);
    }

    private Task<int> ExecuteUpdateAsync(
        string tenantId,
        MigrationStepDto step,
        MigrationExecutionOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Updating entities for step {StepId}", step.StepId);

        // TODO: Implement actual entity update using repository
        // This is a placeholder implementation
        // In a real implementation, this would:
        // 1. Find entities matching step.Target
        // 2. Apply step.Data as attribute updates
        // 3. Update rtBlueprintAppliedAt

        if (options.DryRun)
        {
            _logger.LogDebug("DryRun: Would update entities");
            return Task.FromResult(0);
        }

        return Task.FromResult(1);
    }

    private Task<int> ExecuteDeleteAsync(
        string tenantId,
        MigrationStepDto step,
        MigrationExecutionOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting entities for step {StepId}", step.StepId);

        // TODO: Implement actual entity deletion using repository
        // This is a placeholder implementation
        // In a real implementation, this would:
        // 1. Find entities matching step.Target
        // 2. If BlueprintSourceOnly, only delete entities where rtBlueprintSource matches
        // 3. Delete the entities

        if (options.DryRun)
        {
            _logger.LogDebug("DryRun: Would delete entities");
            return Task.FromResult(0);
        }

        return Task.FromResult(1);
    }

    private Task<int> ExecuteRenameAsync(
        string tenantId,
        MigrationStepDto step,
        MigrationExecutionOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Renaming for step {StepId}", step.StepId);

        // TODO: Implement actual rename using repository
        if (options.DryRun)
        {
            _logger.LogDebug("DryRun: Would rename");
            return Task.FromResult(0);
        }

        return Task.FromResult(1);
    }

    private Task<int> ExecuteTransformAsync(
        string tenantId,
        MigrationStepDto step,
        MigrationExecutionOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Transforming for step {StepId}", step.StepId);

        if (step.Transform == null)
        {
            throw new InvalidOperationException("Transform action requires Transform configuration");
        }

        // TODO: Implement actual transform using repository
        // Based on step.Transform.Type:
        // - Rename: Rename attribute
        // - Copy: Copy attribute value to new attribute
        // - Delete: Remove attribute
        // - SetValue: Set static value
        // - MapValue: Transform value using mapping table

        if (options.DryRun)
        {
            _logger.LogDebug("DryRun: Would transform");
            return Task.FromResult(0);
        }

        return Task.FromResult(1);
    }

    private Task<bool> EvaluateConditionAsync(
        string tenantId,
        MigrationConditionDto condition,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Evaluating condition: {ConditionType}", condition.Type);

        // TODO: Implement actual condition evaluation using repository
        // This is a placeholder that always returns true
        // In a real implementation, this would:
        // - EntityExists: Check if entity matching Target exists
        // - EntityNotExists: Check if entity matching Target does not exist
        // - AttributeEquals: Check if attribute has expected value
        // - Custom: Evaluate custom expression

        return Task.FromResult(true);
    }

    private async Task<(bool Success, string? Message)> RunValidationAsync(
        string tenantId,
        MigrationValidationDto validation,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Running validation: {ValidationId}", validation.ValidationId);

        // TODO: Implement actual validation using repository
        // This is a placeholder that always succeeds
        // In a real implementation, this would:
        // - EntityCount: Count entities and compare to ExpectedCount
        // - EntityExists: Check entity exists
        // - AttributeValue: Check attribute value
        // - ReferenceIntegrity: Verify associations are valid

        await Task.CompletedTask;
        return (true, null);
    }

    private Task ValidateStepAsync(
        MigrationStepDto step,
        MigrationValidationResult result,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(step.StepId))
        {
            result.Errors.Add(new MigrationValidationIssue
            {
                Message = "Step ID is required",
                PropertyPath = "steps"
            });
        }

        if (step.Target == null)
        {
            result.Errors.Add(new MigrationValidationIssue
            {
                StepId = step.StepId,
                Message = "Target is required for step",
                PropertyPath = "target"
            });
        }
        else
        {
            // Validate target has at least one selector
            if (string.IsNullOrEmpty(step.Target.CkTypeId) &&
                string.IsNullOrEmpty(step.Target.RtId) &&
                string.IsNullOrEmpty(step.Target.RtWellKnownName) &&
                step.Target.Filter == null)
            {
                result.Warnings.Add(new MigrationValidationIssue
                {
                    StepId = step.StepId,
                    Message = "Target has no selector - may match all entities",
                    PropertyPath = "target"
                });
            }
        }

        // Validate action-specific requirements
        switch (step.Action)
        {
            case MigrationActionType.Add:
                if (step.Data == null)
                {
                    result.Errors.Add(new MigrationValidationIssue
                    {
                        StepId = step.StepId,
                        Message = "Data is required for Add action",
                        PropertyPath = "data"
                    });
                }
                break;

            case MigrationActionType.Transform:
                if (step.Transform == null)
                {
                    result.Errors.Add(new MigrationValidationIssue
                    {
                        StepId = step.StepId,
                        Message = "Transform configuration is required for Transform action",
                        PropertyPath = "transform"
                    });
                }
                break;
        }

        // Validate condition if present
        if (step.Condition != null)
        {
            ValidateCondition(step.Condition, result, $"steps/{step.StepId}/condition");
        }

        return Task.CompletedTask;
    }

    private static void ValidateCondition(
        MigrationConditionDto condition,
        MigrationValidationResult result,
        string path)
    {
        if (condition.Type == MigrationConditionType.Custom && string.IsNullOrEmpty(condition.Expression))
        {
            result.Errors.Add(new MigrationValidationIssue
            {
                Message = "Expression is required for Custom condition type",
                PropertyPath = path
            });
        }

        if ((condition.Type == MigrationConditionType.EntityExists ||
             condition.Type == MigrationConditionType.EntityNotExists) &&
            condition.Target == null)
        {
            result.Errors.Add(new MigrationValidationIssue
            {
                Message = "Target is required for entity existence conditions",
                PropertyPath = path
            });
        }

        if (condition.Type == MigrationConditionType.AttributeEquals &&
            (string.IsNullOrEmpty(condition.Attribute) || condition.Value == null))
        {
            result.Errors.Add(new MigrationValidationIssue
            {
                Message = "Attribute and Value are required for AttributeEquals condition",
                PropertyPath = path
            });
        }
    }

    private static bool IsValidVersion(string version)
    {
        return Version.TryParse(version, out _);
    }
}
