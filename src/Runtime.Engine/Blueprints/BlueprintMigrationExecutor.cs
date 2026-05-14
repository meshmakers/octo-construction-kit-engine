using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Meshmakers.Octo.Runtime.Contracts.Exchange;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
/// Executes blueprint migration scripts against a tenant repository.
/// </summary>
internal class BlueprintMigrationExecutor : IBlueprintMigrationExecutor
{
    private static readonly RtCkId<CkAttributeId> RtBlueprintSourceAttrId =
        new("System/RtBlueprintSource");
    private static readonly RtCkId<CkAttributeId> RtBlueprintLockedAttrId =
        new("System/RtBlueprintLocked");
    private static readonly RtCkId<CkAttributeId> RtBlueprintAppliedAtAttrId =
        new("System/RtBlueprintAppliedAt");

    private readonly IRuntimeRepositoryProvider _runtimeRepositoryProvider;
    private readonly IImportRtModelCommand _importRtModelCommand;
    private readonly ILogger<BlueprintMigrationExecutor> _logger;

    /// <summary>
    /// Creates a new instance of <see cref="BlueprintMigrationExecutor"/>
    /// </summary>
    public BlueprintMigrationExecutor(
        IRuntimeRepositoryProvider runtimeRepositoryProvider,
        IImportRtModelCommand importRtModelCommand,
        ILogger<BlueprintMigrationExecutor> logger)
    {
        _runtimeRepositoryProvider = runtimeRepositoryProvider;
        _importRtModelCommand = importRtModelCommand;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BlueprintMigrationExecutionResult> ExecuteAsync(
        string tenantId,
        BlueprintMigrationDto migration,
        BlueprintMigrationExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new BlueprintMigrationExecutionOptions();

        _logger.LogInformation(
            "Executing migration from {SourceVersion} to {TargetVersion} for tenant {TenantId} (DryRun: {DryRun})",
            migration.SourceVersion,
            migration.TargetVersion,
            tenantId,
            options.DryRun);

        var result = new BlueprintMigrationExecutionResult
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
    public async Task<BlueprintMigrationValidationResult> ValidateAsync(
        string tenantId,
        BlueprintMigrationDto migration,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Validating migration from {SourceVersion} to {TargetVersion}",
            migration.SourceVersion, migration.TargetVersion);

        var result = new BlueprintMigrationValidationResult { IsValid = true };

        // Validate version format
        if (!IsValidVersion(migration.SourceVersion))
        {
            result.Errors.Add(new BlueprintMigrationValidationIssue
            {
                Message = $"Invalid source version format: {migration.SourceVersion}",
                PropertyPath = "sourceVersion"
            });
        }

        if (!IsValidVersion(migration.TargetVersion))
        {
            result.Errors.Add(new BlueprintMigrationValidationIssue
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
                    result.Errors.Add(new BlueprintMigrationValidationIssue
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

    private async Task<BlueprintMigrationStepResult> ExecuteStepAsync(
        string tenantId,
        MigrationStepDto step,
        BlueprintMigrationExecutionOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Executing step {StepId}: {Action}", step.StepId, step.Action);

        var result = new BlueprintMigrationStepResult { StepId = step.StepId };

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

    private async Task<int> ExecuteAddAsync(
        string tenantId,
        MigrationStepDto step,
        BlueprintMigrationExecutionOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Adding entity for step {StepId}", step.StepId);

        if (step.Data == null)
        {
            throw new InvalidOperationException("Add action requires step.Data containing the entity payload");
        }

        var entityDto = step.Data.Value.Deserialize<RtEntityTcDto>(SerializerOptions)
            ?? throw new InvalidOperationException("Could not deserialise step.Data into RtEntityTcDto");

        // Inherit type id from the target if the data did not carry one.
        if (entityDto.CkTypeId == null && !string.IsNullOrEmpty(step.Target.CkTypeId))
        {
            entityDto.CkTypeId = new RtCkId<CkTypeId>(step.Target.CkTypeId!);
        }
        if (entityDto.CkTypeId == null)
        {
            throw new InvalidOperationException(
                $"Add step '{step.StepId}' must specify ckTypeId either in target or in data");
        }

        StampBlueprintProvenance(entityDto, options.BlueprintSource);

        if (options.DryRun)
        {
            _logger.LogDebug("DryRun: would add entity of type {CkTypeId}", entityDto.CkTypeId);
            return 0;
        }

        var repository = await RequireRepositoryAsync(tenantId, cancellationToken).ConfigureAwait(false);

        var root = new RtModelRootTcDto
        {
            Entities = [entityDto]
        };

        await _importRtModelCommand.ImportModelAsync(
            repository, root, ImportStrategy.Upsert, cancellationToken).ConfigureAwait(false);

        return 1;
    }

    private async Task<int> ExecuteUpdateAsync(
        string tenantId,
        MigrationStepDto step,
        BlueprintMigrationExecutionOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Updating entities for step {StepId}", step.StepId);

        if (step.Data == null)
        {
            throw new InvalidOperationException("Update action requires step.Data containing attribute updates");
        }

        var attributeUpdates = ParseAttributeUpdates(step.Data.Value);

        var repository = await RequireRepositoryAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var resolved = await ResolveTargetEntitiesAsync(
            repository, step.Target, options.BlueprintSource, cancellationToken).ConfigureAwait(false);

        if (resolved.Count == 0)
        {
            _logger.LogDebug("Step {StepId}: no entities matched target", step.StepId);
            return 0;
        }

        if (options.DryRun)
        {
            _logger.LogDebug("DryRun: would update {Count} entities for step {StepId}",
                resolved.Count, step.StepId);
            return 0;
        }

        var session = await repository.GetSessionAsync().ConfigureAwait(false);
        var affected = 0;

        foreach (var (ckTypeId, entity) in resolved)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var kvp in attributeUpdates)
            {
                entity.SetAttributeRawValue(kvp.Key, kvp.Value);
            }
            entity.SetAttributeRawValue(RtBlueprintAppliedAtAttrId.ElementId.Name, DateTime.UtcNow);

            await repository.ReplaceOneRtEntityByIdAsync(session, ckTypeId, entity.RtId, entity)
                .ConfigureAwait(false);
            affected++;
        }

        return affected;
    }

    private async Task<int> ExecuteDeleteAsync(
        string tenantId,
        MigrationStepDto step,
        BlueprintMigrationExecutionOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting entities for step {StepId}", step.StepId);

        var repository = await RequireRepositoryAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var resolved = await ResolveTargetEntitiesAsync(
            repository, step.Target, options.BlueprintSource, cancellationToken).ConfigureAwait(false);

        if (resolved.Count == 0)
        {
            return 0;
        }

        if (options.DryRun)
        {
            _logger.LogDebug("DryRun: would delete {Count} entities for step {StepId}",
                resolved.Count, step.StepId);
            return 0;
        }

        var session = await repository.GetSessionAsync().ConfigureAwait(false);
        var deleted = 0;

        foreach (var (ckTypeId, entity) in resolved)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await repository.DeleteOneRtEntityByRtIdAsync(
                session, ckTypeId, entity.RtId, DeleteOptions.Erase).ConfigureAwait(false);
            deleted++;
        }

        return deleted;
    }

    private Task<int> ExecuteRenameAsync(
        string tenantId,
        MigrationStepDto step,
        BlueprintMigrationExecutionOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Rename step {StepId}", step.StepId);
        // Rename is an alias for Transform with Type=Rename — both paths reach
        // ExecuteTransformAsync which carries SourceAttribute / TargetAttribute.
        if (step.Transform == null)
        {
            throw new InvalidOperationException(
                $"Rename step '{step.StepId}' requires a Transform configuration with SourceAttribute and TargetAttribute");
        }
        if (step.Transform.Type != TransformType.Rename)
        {
            _logger.LogDebug(
                "Step {StepId} action=Rename but transform type is {TransformType}; running anyway",
                step.StepId, step.Transform.Type);
        }
        return ExecuteTransformAsync(tenantId, step, options, cancellationToken);
    }

    private async Task<int> ExecuteTransformAsync(
        string tenantId,
        MigrationStepDto step,
        BlueprintMigrationExecutionOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Transforming for step {StepId}", step.StepId);

        if (step.Transform == null)
        {
            throw new InvalidOperationException("Transform action requires Transform configuration");
        }

        var transform = step.Transform;
        var repository = await RequireRepositoryAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var resolved = await ResolveTargetEntitiesAsync(
            repository, step.Target, options.BlueprintSource, cancellationToken).ConfigureAwait(false);

        if (resolved.Count == 0)
        {
            return 0;
        }

        if (options.DryRun)
        {
            _logger.LogDebug(
                "DryRun: would transform ({Type}) {Count} entities for step {StepId}",
                transform.Type, resolved.Count, step.StepId);
            return 0;
        }

        var session = await repository.GetSessionAsync().ConfigureAwait(false);
        var affected = 0;

        foreach (var (ckTypeId, entity) in resolved)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mutated = ApplyTransform(entity, transform);
            if (!mutated)
            {
                continue;
            }
            entity.SetAttributeRawValue(RtBlueprintAppliedAtAttrId.ElementId.Name, DateTime.UtcNow);
            await repository.ReplaceOneRtEntityByIdAsync(session, ckTypeId, entity.RtId, entity)
                .ConfigureAwait(false);
            affected++;
        }

        return affected;
    }

    private static bool ApplyTransform(RtEntity entity, TransformConfigDto transform)
    {
        switch (transform.Type)
        {
            case TransformType.Rename:
            {
                if (string.IsNullOrEmpty(transform.SourceAttribute)
                    || string.IsNullOrEmpty(transform.TargetAttribute))
                {
                    throw new InvalidOperationException(
                        "Rename transform requires SourceAttribute and TargetAttribute");
                }
                if (!entity.Attributes.TryGetValue(transform.SourceAttribute!, out var value))
                {
                    return false;
                }
                entity.SetAttributeRawValue(transform.TargetAttribute!, value);
                entity.SetAttributeRawValue(transform.SourceAttribute!, null);
                return true;
            }
            case TransformType.Copy:
            {
                if (string.IsNullOrEmpty(transform.SourceAttribute)
                    || string.IsNullOrEmpty(transform.TargetAttribute))
                {
                    throw new InvalidOperationException(
                        "Copy transform requires SourceAttribute and TargetAttribute");
                }
                if (!entity.Attributes.TryGetValue(transform.SourceAttribute!, out var value))
                {
                    return false;
                }
                entity.SetAttributeRawValue(transform.TargetAttribute!, value);
                return true;
            }
            case TransformType.Delete:
            {
                if (string.IsNullOrEmpty(transform.SourceAttribute))
                {
                    throw new InvalidOperationException("Delete transform requires SourceAttribute");
                }
                if (!entity.Attributes.ContainsKey(transform.SourceAttribute!))
                {
                    return false;
                }
                entity.SetAttributeRawValue(transform.SourceAttribute!, null);
                return true;
            }
            case TransformType.SetValue:
            {
                var attribute = transform.TargetAttribute ?? transform.SourceAttribute;
                if (string.IsNullOrEmpty(attribute))
                {
                    throw new InvalidOperationException(
                        "SetValue transform requires TargetAttribute (or SourceAttribute) to identify the destination");
                }
                entity.SetAttributeRawValue(attribute!, transform.Value);
                return true;
            }
            case TransformType.MapValue:
            {
                if (string.IsNullOrEmpty(transform.SourceAttribute))
                {
                    throw new InvalidOperationException("MapValue transform requires SourceAttribute");
                }
                if (transform.ValueMapping == null || transform.ValueMapping.Count == 0)
                {
                    throw new InvalidOperationException("MapValue transform requires ValueMapping entries");
                }
                if (!entity.Attributes.TryGetValue(transform.SourceAttribute!, out var current))
                {
                    return false;
                }

                var key = current?.ToString();
                if (key != null && transform.ValueMapping.TryGetValue(key, out var mapped))
                {
                    var dst = transform.TargetAttribute ?? transform.SourceAttribute!;
                    entity.SetAttributeRawValue(dst, mapped);
                    return true;
                }
                return false;
            }
            default:
                throw new InvalidOperationException($"Unsupported transform type: {transform.Type}");
        }
    }

    private async Task<bool> EvaluateConditionAsync(
        string tenantId,
        MigrationConditionDto condition,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Evaluating condition: {ConditionType}", condition.Type);

        switch (condition.Type)
        {
            case MigrationConditionType.Custom:
                _logger.LogWarning(
                    "Custom condition is not yet supported by the migration executor; treating as 'true'");
                return true;

            case MigrationConditionType.EntityExists:
            case MigrationConditionType.EntityNotExists:
            {
                if (condition.Target == null)
                {
                    throw new InvalidOperationException(
                        $"Condition '{condition.Type}' requires a target");
                }
                var repository = await RequireRepositoryAsync(tenantId, cancellationToken).ConfigureAwait(false);
                var matches = await ResolveTargetEntitiesAsync(
                    repository, condition.Target, blueprintSource: null, cancellationToken)
                    .ConfigureAwait(false);

                return condition.Type == MigrationConditionType.EntityExists
                    ? matches.Count > 0
                    : matches.Count == 0;
            }

            case MigrationConditionType.AttributeEquals:
            {
                if (condition.Target == null
                    || string.IsNullOrEmpty(condition.Attribute))
                {
                    throw new InvalidOperationException(
                        "AttributeEquals condition requires Target and Attribute");
                }

                var repository = await RequireRepositoryAsync(tenantId, cancellationToken).ConfigureAwait(false);
                var matches = await ResolveTargetEntitiesAsync(
                    repository, condition.Target, blueprintSource: null, cancellationToken)
                    .ConfigureAwait(false);

                if (matches.Count == 0)
                {
                    return false;
                }

                return matches.All(m =>
                {
                    var (_, entity) = m;
                    if (!entity.Attributes.TryGetValue(condition.Attribute!, out var actual))
                    {
                        return condition.Value == null;
                    }
                    return Equals(actual?.ToString(), condition.Value?.ToString());
                });
            }

            default:
                throw new InvalidOperationException($"Unsupported condition type: {condition.Type}");
        }
    }

    private async Task<(bool Success, string? Message)> RunValidationAsync(
        string tenantId,
        MigrationValidationDto validation,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Running validation: {ValidationId}", validation.ValidationId);

        switch (validation.Type)
        {
            case MigrationValidationType.ReferenceIntegrity:
                return (true,
                    "ReferenceIntegrity validation is not yet implemented; skipped");

            case MigrationValidationType.EntityCount:
            case MigrationValidationType.EntityExists:
            {
                if (validation.Target == null)
                {
                    return (false, $"Validation '{validation.ValidationId}' requires a target");
                }

                var repository = await RequireRepositoryAsync(tenantId, cancellationToken).ConfigureAwait(false);
                var matches = await ResolveTargetEntitiesAsync(
                    repository, validation.Target, blueprintSource: null, cancellationToken)
                    .ConfigureAwait(false);

                if (validation.Type == MigrationValidationType.EntityExists)
                {
                    return matches.Count > 0
                        ? (true, null)
                        : (false, $"Validation '{validation.ValidationId}': no matching entities");
                }

                var expected = validation.ExpectedCount ?? 0;
                return matches.Count == expected
                    ? (true, null)
                    : (false,
                        $"Validation '{validation.ValidationId}': expected {expected} entities, found {matches.Count}");
            }

            case MigrationValidationType.AttributeValue:
            {
                if (validation.Target == null || string.IsNullOrEmpty(validation.Target.RtWellKnownName))
                {
                    return (false,
                        $"Validation '{validation.ValidationId}' requires a target with rtWellKnownName");
                }

                var repository = await RequireRepositoryAsync(tenantId, cancellationToken).ConfigureAwait(false);
                var matches = await ResolveTargetEntitiesAsync(
                    repository, validation.Target, blueprintSource: null, cancellationToken)
                    .ConfigureAwait(false);

                if (matches.Count == 0)
                {
                    return (false, $"Validation '{validation.ValidationId}': no matching entities");
                }

                var attribute = validation.Target.RtWellKnownName!;
                var entity = matches[0].Entity;
                if (!entity.Attributes.TryGetValue(attribute, out var actual))
                {
                    return (false,
                        $"Validation '{validation.ValidationId}': attribute '{attribute}' not present");
                }

                return Equals(actual?.ToString(), validation.ExpectedValue?.ToString())
                    ? (true, null)
                    : (false,
                        $"Validation '{validation.ValidationId}': attribute '{attribute}' is '{actual}', expected '{validation.ExpectedValue}'");
            }

            default:
                return (false, $"Unsupported validation type: {validation.Type}");
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private void StampBlueprintProvenance(RtEntityTcDto entity, string? blueprintSource)
    {
        if (!string.IsNullOrEmpty(blueprintSource))
        {
            SetOrReplaceAttribute(entity, RtBlueprintSourceAttrId, blueprintSource!);
        }
        SetOrReplaceAttribute(entity, RtBlueprintAppliedAtAttrId, DateTime.UtcNow);
        // Migration-created entities are blueprint-managed by default. The seed
        // payload can override by passing rtBlueprintLocked = false explicitly.
        if (entity.Attributes.All(a => !a.Id.Equals(RtBlueprintLockedAttrId)))
        {
            entity.Attributes.Add(new RtAttributeTcDto { Id = RtBlueprintLockedAttrId, Value = true });
        }
    }

    private static void SetOrReplaceAttribute(
        RtEntityTcDto entity, RtCkId<CkAttributeId> attributeId, object value)
    {
        var existing = entity.Attributes.FirstOrDefault(a => a.Id.Equals(attributeId));
        if (existing != null)
        {
            existing.Value = value;
        }
        else
        {
            entity.Attributes.Add(new RtAttributeTcDto { Id = attributeId, Value = value });
        }
    }

    private static Dictionary<string, object?> ParseAttributeUpdates(JsonElement data)
    {
        var updates = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (data.ValueKind != JsonValueKind.Object)
        {
            return updates;
        }
        foreach (var property in data.EnumerateObject())
        {
            updates[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number when property.Value.TryGetInt64(out var l) => (object)l,
                JsonValueKind.Number => property.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => property.Value.GetRawText()
            };
        }
        return updates;
    }

    private async Task<IRuntimeRepository> RequireRepositoryAsync(
        string tenantId, CancellationToken cancellationToken)
    {
        var repository = await _runtimeRepositoryProvider
            .GetRepositoryAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return repository ?? throw new InvalidOperationException(
            $"No runtime repository available for tenant '{tenantId}'");
    }

    /// <summary>
    /// Resolves the entities that match an <see cref="EntityTargetDto"/>. Walks
    /// only one CK type (the resolver does not span the entire schema) — the
    /// caller must therefore put the right <c>CkTypeId</c> on the target.
    /// </summary>
    private async Task<List<TargetMatch>> ResolveTargetEntitiesAsync(
        IRuntimeRepository repository,
        EntityTargetDto target,
        string? blueprintSource,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(target.CkTypeId))
        {
            throw new InvalidOperationException(
                "Migration target must specify a ckTypeId — the executor does not scan all CK types");
        }

        var ckTypeId = new RtCkId<CkTypeId>(target.CkTypeId!);
        var queryOptions = RtEntityQueryOptions.Create();

        var session = await repository.GetSessionAsync().ConfigureAwait(false);
        var resultSet = await repository.GetRtEntitiesByTypeAsync(session, ckTypeId, queryOptions)
            .ConfigureAwait(false);

        IEnumerable<RtEntity> candidates = resultSet.Items;

        if (!string.IsNullOrEmpty(target.RtId))
        {
            candidates = candidates.Where(e => e.RtId.ToString() == target.RtId);
        }
        if (!string.IsNullOrEmpty(target.RtWellKnownName))
        {
            candidates = candidates.Where(e => e.RtWellKnownName == target.RtWellKnownName);
        }
        if (target.Filter != null)
        {
            candidates = candidates.Where(e => EvaluateFilter(e, target.Filter));
        }
        if (target.BlueprintSourceOnly && !string.IsNullOrEmpty(blueprintSource))
        {
            candidates = candidates.Where(e =>
                e.GetAttributeStringValueOrDefault(RtBlueprintSourceAttrId.ElementId.Name) == blueprintSource);
        }

        return candidates.Select(e => new TargetMatch(ckTypeId, e)).ToList();
    }

    private static bool EvaluateFilter(RtEntity entity, FilterExpressionDto filter)
    {
        if (filter.And != null && filter.And.Count > 0)
        {
            return filter.And.All(f => EvaluateFilter(entity, f));
        }
        if (filter.Or != null && filter.Or.Count > 0)
        {
            return filter.Or.Any(f => EvaluateFilter(entity, f));
        }

        if (string.IsNullOrEmpty(filter.Attribute) || filter.Operator == null)
        {
            return true;
        }

        var hasValue = entity.Attributes.TryGetValue(filter.Attribute!, out var raw);
        var actual = raw?.ToString();
        var expected = filter.Value?.ToString();

        return filter.Operator switch
        {
            FilterOperator.Eq => string.Equals(actual, expected, StringComparison.Ordinal),
            FilterOperator.Ne => !string.Equals(actual, expected, StringComparison.Ordinal),
            FilterOperator.Contains => actual != null && expected != null && actual.Contains(expected),
            FilterOperator.StartsWith => actual != null && expected != null && actual.StartsWith(expected),
            FilterOperator.EndsWith => actual != null && expected != null && actual.EndsWith(expected),
            FilterOperator.Exists => hasValue && raw != null,
            FilterOperator.NotExists => !hasValue || raw == null,
            _ => false
        };
    }

    private readonly record struct TargetMatch(RtCkId<CkTypeId> CkTypeId, RtEntity Entity);

    private Task ValidateStepAsync(
        MigrationStepDto step,
        BlueprintMigrationValidationResult result,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(step.StepId))
        {
            result.Errors.Add(new BlueprintMigrationValidationIssue
            {
                Message = "Step ID is required",
                PropertyPath = "steps"
            });
        }

        if (step.Target == null)
        {
            result.Errors.Add(new BlueprintMigrationValidationIssue
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
                result.Warnings.Add(new BlueprintMigrationValidationIssue
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
                    result.Errors.Add(new BlueprintMigrationValidationIssue
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
                    result.Errors.Add(new BlueprintMigrationValidationIssue
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
        BlueprintMigrationValidationResult result,
        string path)
    {
        if (condition.Type == MigrationConditionType.Custom && string.IsNullOrEmpty(condition.Expression))
        {
            result.Errors.Add(new BlueprintMigrationValidationIssue
            {
                Message = "Expression is required for Custom condition type",
                PropertyPath = path
            });
        }

        if ((condition.Type == MigrationConditionType.EntityExists ||
             condition.Type == MigrationConditionType.EntityNotExists) &&
            condition.Target == null)
        {
            result.Errors.Add(new BlueprintMigrationValidationIssue
            {
                Message = "Target is required for entity existence conditions",
                PropertyPath = path
            });
        }

        if (condition.Type == MigrationConditionType.AttributeEquals &&
            (string.IsNullOrEmpty(condition.Attribute) || condition.Value == null))
        {
            result.Errors.Add(new BlueprintMigrationValidationIssue
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
