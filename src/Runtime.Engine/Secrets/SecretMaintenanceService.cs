using System.Collections;
using System.Diagnostics;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Secrets;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.Secrets;

/// <summary>
///     Implementation of <see cref="ISecretMaintenanceService" /> (AB#5532) over the repository
///     abstractions: <see cref="IRuntimeRepositoryProvider" />, the CK cache and
///     <see cref="IRuntimeRepository.RewriteAttributeValueForMigrationAsync" />. Logs names and counts,
///     never a value.
/// </summary>
internal sealed class SecretMaintenanceService(
    IRuntimeRepositoryProvider repositoryProvider,
    ICkCacheService ckCacheService,
    ISecretAttributeProtector protector,
    ISecretWriteNormalizer writeNormalizer,
    ILogger<SecretMaintenanceService> logger) : ISecretMaintenanceService
{
    private const string NormalizePlaceholdersModeTag = "normalize_placeholders";

    /// <summary>
    ///     Paging cursor of the sweep: the runtime id (resolved to <c>_id</c> by the MongoDB field resolver).
    /// </summary>
    internal const string RtIdSortPath = "rtId";

    /// <inheritdoc />
    public Task<SecretSweepResult> SweepTenantAsync(string tenantId, SecretSweepMode mode,
        CancellationToken cancellationToken = default)
    {
        return SweepTenantAsync(tenantId, mode, SecretSweepOptions.Default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SecretSweepResult> SweepTenantAsync(string tenantId, SecretSweepMode mode, SecretSweepOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(options);

        if (mode == SecretSweepMode.Decrypt && !options.ConfirmDecrypt)
        {
            throw new InvalidOperationException(
                "The Decrypt sweep writes secrets back as clear text and is an emergency rollback path only; " +
                "it requires SecretSweepOptions.ConfirmDecrypt = true.");
        }

        if (mode != SecretSweepMode.Verify && !protector.IsConfigured)
        {
            throw new SecretEncryptionNotConfiguredException(
                $"The {mode} secret sweep needs the key ring (SecretEncryption:ActiveKeyId / SecretEncryption:Keys), " +
                "which is not configured on this host.");
        }

        return RunAsync(tenantId, mode, options, false, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SecretSweepResult> NormalizePlaceholdersAsync(string tenantId, string? ckModelName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        return RunAsync(tenantId, SecretSweepMode.Verify, new SecretSweepOptions { CkModelName = ckModelName }, true,
            cancellationToken);
    }

    private async Task<SecretSweepResult> RunAsync(string tenantId, SecretSweepMode mode, SecretSweepOptions options,
        bool normalizePlaceholdersOnly, CancellationToken cancellationToken)
    {
        var repository = await repositoryProvider.GetRepositoryAsync(tenantId, cancellationToken).ConfigureAwait(false)
                         ?? throw new InvalidOperationException($"No runtime repository is available for tenant '{tenantId}'.");
        if (!ckCacheService.IsTenantLoaded(tenantId))
        {
            await repository.LoadCacheForTenantAsync(ckCacheService).ConfigureAwait(false);
        }

        var modeTag = normalizePlaceholdersOnly ? NormalizePlaceholdersModeTag : ModeTag(mode);
        var result = new SecretSweepResult(tenantId, mode);
        var countsPerModel = new Dictionary<string, SecretFormCounts>(StringComparer.Ordinal);
        var batchSize = options.BatchSize > 0 ? options.BatchSize : SecretSweepOptions.Default.BatchSize;

        var types = ckCacheService.GetCkTypes(tenantId)
            .Where(t => !t.IsAbstract && writeNormalizer.HasSecretAttributes(ckCacheService, tenantId, t))
            .Where(t => options.CkModelName == null || BelongsToModel(tenantId, t, options.CkModelName))
            .OrderBy(t => t.CkTypeId.FullName, StringComparer.Ordinal)
            .ToList();

        logger.LogInformation("Secret sweep {Mode} of tenant {TenantId}: {TypeCount} CK type(s) with Secret attributes",
            modeTag, tenantId, types.Count);

        var session = await repository.GetSessionAsync().ConfigureAwait(false);
        var processed = new HashSet<OctoObjectId>();
        var slotReports = new Dictionary<string, SecretSlotReport>(StringComparer.Ordinal);

        foreach (var type in types)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.CkTypesScanned++;
            var skip = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Archived entities hold secrets too. Offset paging needs a deterministic order: without a
                // sort the backend may return pages in plan order (index or natural order), which is not
                // guaranteed to be stable across the queries of one sweep, so an entity could be skipped
                // (left as plaintext) or seen twice. rtId ("_id") is indexed and never changes on a
                // rewrite - same cursor as the display rule sweep (AB#5532 review).
                var queryOptions = RtEntityQueryOptions.Create().Global(true).WithCachingDisabled()
                    .SortOrder(RtIdSortPath, SortOrders.Ascending);
                var page = await repository.GetRtEntitiesByTypeAsync(session, type.CkTypeId.ToRtCkId(),
                    queryOptions, skip, batchSize).ConfigureAwait(false);
                var entities = page.Items.ToList();
                if (entities.Count == 0)
                {
                    break;
                }

                foreach (var entity in entities)
                {
                    // A query of a type may return entities of derived types as well; count each once.
                    if (!processed.Add(entity.RtId))
                    {
                        continue;
                    }

                    result.EntitiesScanned++;
                    var entityGraph = entity.CkTypeId != null &&
                                      ckCacheService.TryGetRtCkType(tenantId, entity.CkTypeId, out var actual)
                        ? actual
                        : type;
                    var model = entityGraph.CkTypeId.ModelId.Name;
                    if (!countsPerModel.TryGetValue(model, out var modelCounts))
                    {
                        modelCounts = new SecretFormCounts();
                        countsPerModel[model] = modelCounts;
                    }

                    var context = new SweepContext(tenantId, mode, normalizePlaceholdersOnly, result, modelCounts,
                        slotReports, entityGraph.CkTypeId.ToRtCkId().ToString(), entity.RtId);
                    var rewritten = await ProcessEntityAsync(context, repository, session, entityGraph, entity)
                        .ConfigureAwait(false);
                    if (rewritten)
                    {
                        result.EntitiesRewritten++;
                    }
                }

                if (entities.Count < batchSize)
                {
                    break;
                }

                skip += entities.Count;
            }
        }

        result.Slots.AddRange(slotReports.Values.OrderBy(r => r.CkTypeId, StringComparer.Ordinal)
            .ThenBy(r => r.AttributePath, StringComparer.Ordinal));
        result.CompletedAt = DateTime.UtcNow;

        var tags = new TagList { { "tenant", tenantId }, { "mode", modeTag } };
        if (result.ValuesRewritten > 0)
        {
            SecretDiagnostics.SweepValuesRewritten.Add(result.ValuesRewritten, tags);
        }

        if (result.Totals.Failed > 0)
        {
            SecretDiagnostics.SweepFailures.Add(result.Totals.Failed, tags);
        }

        if (!normalizePlaceholdersOnly && options.CkModelName == null)
        {
            // Only a full sweep describes the tenant completely.
            SecretDiagnostics.RecordSweepValues(tenantId, countsPerModel);
        }

        logger.LogInformation(
            "Secret sweep {Mode} of tenant {TenantId} done: {Entities} entities, {Total} values " +
            "(not set {NotSet}, placeholder {Placeholder}, plaintext {Plaintext}, enc_v1 {EncV1}, enc_v2 {EncV2}, " +
            "unknown kid {UnknownKid}), {Rewritten} rewritten, {Cleared} cleared, {Failed} failed",
            modeTag, tenantId, result.EntitiesScanned, result.Totals.Total, result.Totals.NotSet,
            result.Totals.Placeholder, result.Totals.Plaintext, result.Totals.EncV1, result.Totals.EncV2,
            result.Totals.UnknownKeyId, result.ValuesRewritten, result.Cleared.Count, result.Totals.Failed);

        return result;
    }

    private bool BelongsToModel(string tenantId, CkTypeGraph type, string modelName)
    {
        if (string.Equals(type.CkTypeId.ModelId.Name, modelName, StringComparison.Ordinal))
        {
            return true;
        }

        return type.AllAttributes.Values.Any(a =>
            writeNormalizer.AttributeHasSecrets(ckCacheService, tenantId, a) &&
            string.Equals(a.CkAttributeId.ModelId.Name, modelName, StringComparison.Ordinal));
    }

    private async Task<bool> ProcessEntityAsync(SweepContext context, IRuntimeRepository repository,
        IOctoSession session, CkTypeGraph entityGraph, RtEntity entity)
    {
        var rewrittenAny = false;
        foreach (var attribute in entityGraph.AllAttributes.Values)
        {
            if (!writeNormalizer.AttributeHasSecrets(ckCacheService, context.TenantId, attribute))
            {
                continue;
            }

            entity.Attributes.TryGetValue(attribute.AttributeName, out var value);
            bool changed;
            object? newValue;
            var valuesBefore = context.Result.ValuesRewritten;
            var clearedBefore = context.Result.Cleared.Count;
            if (attribute.ValueType == AttributeValueTypesDto.Secret)
            {
                (changed, newValue) = ProcessSlot(context, attribute.AttributeName, attribute.AttributeName, value);
            }
            else
            {
                newValue = value;
                changed = ProcessRecordValue(context, attribute, value, attribute.AttributeName, attribute.AttributeName);
            }

            if (!changed)
            {
                continue;
            }

            try
            {
                await repository.RewriteAttributeValueForMigrationAsync(session,
                        entity.CkTypeId ?? entityGraph.CkTypeId.ToRtCkId(), entity.RtId, attribute.AttributeName,
                        newValue)
                    .ConfigureAwait(false);
                rewrittenAny = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The rewrite did not happen: take back what was counted for it.
                var rewrittenHere = context.Result.ValuesRewritten - valuesBefore;
                context.Result.ValuesRewritten = valuesBefore;
                if (context.Result.Cleared.Count > clearedBefore)
                {
                    context.Result.Cleared.RemoveRange(clearedBefore, context.Result.Cleared.Count - clearedBefore);
                }

                context.Result.Totals.AddFailure();
                context.ModelCounts.AddFailure();
                context.Result.Failures.Add(new SecretSweepFailure(context.CkTypeId, context.RtId,
                    attribute.AttributeName,
                    $"{ex.GetType().Name} while writing {rewrittenHere} changed value(s) of the attribute"));
                logger.LogWarning(
                    "Secret sweep could not rewrite attribute {AttributeName} of {CkTypeId}@{RtId} (tenant {TenantId}): {ExceptionType}",
                    attribute.AttributeName, context.CkTypeId, context.RtId, context.TenantId, ex.GetType().Name);
            }
        }

        return rewrittenAny;
    }

    /// <summary>
    ///     Walks a record (array) value and processes its Secret sub-values in place. Returns true when a
    ///     sub-value changed, i.e. the whole attribute has to be rewritten.
    /// </summary>
    private bool ProcessRecordValue(SweepContext context, CkTypeAttributeGraph attribute, object? value,
        string reportPath, string elementPath)
    {
        if (value == null)
        {
            return false;
        }

        if (attribute.ValueType == AttributeValueTypesDto.Record)
        {
            return value is RtRecord record &&
                   ProcessRecord(context, attribute, record, reportPath, elementPath);
        }

        if (value is not IEnumerable elements || value is string)
        {
            return false;
        }

        var changed = false;
        var index = 0;
        foreach (var element in elements)
        {
            if (element is RtRecord record)
            {
                var graph = ResolveRecord(context.TenantId, record, attribute);
                changed |= ProcessRecord(context, attribute, record, reportPath + "[]",
                    SecretWriteNormalizer.BuildElementPath(elementPath, graph?.RecordKey, record, index), graph);
            }

            index++;
        }

        return changed;
    }

    private bool ProcessRecord(SweepContext context, CkTypeAttributeGraph attribute, RtRecord record,
        string reportPath, string elementPath, CkRecordGraph? graph = null)
    {
        graph ??= ResolveRecord(context.TenantId, record, attribute);
        if (graph == null)
        {
            return false;
        }

        var changed = false;
        foreach (var member in graph.AllAttributes.Values)
        {
            if (!writeNormalizer.AttributeHasSecrets(ckCacheService, context.TenantId, member))
            {
                continue;
            }

            var memberReportPath = reportPath + "." + member.AttributeName;
            var memberElementPath = elementPath + "." + member.AttributeName;
            record.Attributes.TryGetValue(member.AttributeName, out var memberValue);
            if (member.ValueType == AttributeValueTypesDto.Secret)
            {
                var (memberChanged, newValue) = ProcessSlot(context, memberReportPath, memberElementPath, memberValue);
                if (memberChanged)
                {
                    record.SetAttributeRawValue(member.AttributeName, newValue);
                    changed = true;
                }
            }
            else
            {
                changed |= ProcessRecordValue(context, member, memberValue, memberReportPath, memberElementPath);
            }
        }

        return changed;
    }

    private CkRecordGraph? ResolveRecord(string tenantId, RtRecord record, CkTypeAttributeGraph attribute)
    {
        if (record.CkRecordId is { IsEmpty: false } &&
            ckCacheService.TryGetRtCkRecord(tenantId, record.CkRecordId, out var graph))
        {
            return graph;
        }

        return attribute.ValueCkRecordId != null &&
               ckCacheService.TryGetCkRecord(tenantId, attribute.ValueCkRecordId, out var declared)
            ? declared
            : null;
    }

    /// <summary>
    ///     Classifies one Secret slot, counts it and decides the new value for the sweep mode.
    /// </summary>
    private (bool Changed, object? NewValue) ProcessSlot(SweepContext context, string reportPath, string elementPath,
        object? value)
    {
        var slot = context.GetSlot(reportPath);
        var (form, keyId, raw) = Classify(value);
        if (form == null)
        {
            // Not a value a Secret slot can hold - count it as a failure without touching it.
            slot.Counts.AddFailure();
            context.Result.Totals.AddFailure();
            context.ModelCounts.AddFailure();
            context.Result.Failures.Add(new SecretSweepFailure(context.CkTypeId, context.RtId, elementPath,
                $"Unexpected value type '{value!.GetType().Name}' in a Secret slot"));
            return (false, null);
        }

        slot.Counts.Add(form.Value, keyId);
        context.Result.Totals.Add(form.Value, keyId);
        context.ModelCounts.Add(form.Value, keyId);

        try
        {
            var (changed, newValue, cleared) = Decide(context, form.Value, keyId, raw, value);
            if (changed)
            {
                context.Result.ValuesRewritten++;
                if (cleared)
                {
                    context.Result.Cleared.Add(new SecretSweepClearedValue(context.CkTypeId, context.RtId,
                        elementPath, form.Value, keyId));
                }
            }

            return (changed, newValue);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            slot.Counts.AddFailure();
            context.Result.Totals.AddFailure();
            context.ModelCounts.AddFailure();
            context.Result.Failures.Add(new SecretSweepFailure(context.CkTypeId, context.RtId, elementPath,
                $"{ex.GetType().Name} while processing a {form.Value} value"));
            logger.LogWarning(
                "Secret sweep could not process {AttributePath} of {CkTypeId}@{RtId} (tenant {TenantId}, form {Form}): {ExceptionType}",
                elementPath, context.CkTypeId, context.RtId, context.TenantId, form.Value, ex.GetType().Name);
            return (false, null);
        }
    }

    private (bool Changed, object? NewValue, bool Cleared) Decide(SweepContext context, SecretValueForm form,
        string? keyId, string? raw, object? value)
    {
        var access = new SecretAccessContext(context.TenantId, context.CkTypeId, null, "secret-sweep");
        if (context.NormalizePlaceholdersOnly)
        {
            return form == SecretValueForm.Placeholder ? (true, null, true) : (false, null, false);
        }

        switch (context.Mode)
        {
            case SecretSweepMode.Verify:
                return (false, null, false);

            case SecretSweepMode.Encrypt:
            case SecretSweepMode.Reprotect:
                switch (form)
                {
                    case SecretValueForm.Placeholder:
                        return (true, null, true);
                    case SecretValueForm.Plaintext:
                        return (true, protector.Protect(raw!), false);
                    case SecretValueForm.EncV1:
                        return (true, protector.Reprotect(RtSecretValue.LegacyPlaintext(raw!), access), false);
                    case SecretValueForm.EncV2:
                    {
                        // An enc:v2 envelope kept in a string slot becomes a protected value.
                        var storedAsString = value is not RtSecretValue { IsProtected: true };
                        var stored = value is RtSecretValue { IsProtected: true } protectedValue
                            ? protectedValue
                            : RtSecretValue.Protected(raw!);
                        if (context.Mode == SecretSweepMode.Reprotect && protector.NeedsReprotect(stored))
                        {
                            return (true, protector.Reprotect(stored, access), false);
                        }

                        // Encrypt leaves enc:v2 alone; a string-stored envelope moves to the sub-document form.
                        return storedAsString ? (true, stored, false) : (false, null, false);
                    }
                    default:
                        return (false, null, false);
                }

            case SecretSweepMode.ClearUnknownKid:
                return form == SecretValueForm.UnknownKeyId ? (true, null, true) : (false, null, false);

            case SecretSweepMode.Decrypt:
                switch (form)
                {
                    case SecretValueForm.EncV1:
                    case SecretValueForm.EncV2:
                        // EMERGENCY path: clear text goes back into a string slot (concept §5.2 rollback).
                        return (true, protector.Unprotect(raw!, access), false);
                    case SecretValueForm.UnknownKeyId:
                        throw new UnknownSecretKeyIdException(keyId ?? string.Empty);
                    default:
                        return (false, null, false);
                }

            default:
                return (false, null, false);
        }
    }

    /// <summary>
    ///     Classifies a stored Secret value. Returns the raw stored text for legacy values and the
    ///     envelope for protected ones (needed by the actions); <c>form == null</c> for a value a Secret
    ///     slot cannot hold.
    /// </summary>
    private (SecretValueForm? Form, string? KeyId, string? Raw) Classify(object? value)
    {
        string raw;
        switch (value)
        {
            case null:
                return (SecretValueForm.NotSet, null, null);
            case RtSecretValue { IsProtected: true } protectedValue:
            {
                var kid = protectedValue.KeyId;
                return (protector.IsKnownKeyId(kid) ? SecretValueForm.EncV2 : SecretValueForm.UnknownKeyId, kid,
                    protectedValue.Envelope);
            }
            case RtSecretValue secret:
                // LegacyPlaintext (and a Pending value that should never be stored).
                raw = secret.RawValue;
                break;
            case string text:
                raw = text;
                break;
            default:
                return (null, null, null);
        }

        if (raw.Length == 0 || SecretAttributeConventions.IsPlaceholder(raw))
        {
            return (SecretValueForm.Placeholder, null, raw);
        }

        if (SecretEnvelope.TryParse(raw, out var info))
        {
            if (info.Version == 1)
            {
                return (SecretValueForm.EncV1, null, raw);
            }

            return (protector.IsKnownKeyId(info.KeyId) ? SecretValueForm.EncV2 : SecretValueForm.UnknownKeyId,
                info.KeyId, raw);
        }

        return (SecretValueForm.Plaintext, null, raw);
    }

    private static string ModeTag(SecretSweepMode mode)
    {
        return mode switch
        {
            SecretSweepMode.Verify => "verify",
            SecretSweepMode.Encrypt => "encrypt",
            SecretSweepMode.Reprotect => "reprotect",
            SecretSweepMode.ClearUnknownKid => "clear_unknown_kid",
            SecretSweepMode.Decrypt => "decrypt",
            _ => mode.ToString()
        };
    }

    private sealed record SweepContext(
        string TenantId,
        SecretSweepMode Mode,
        bool NormalizePlaceholdersOnly,
        SecretSweepResult Result,
        SecretFormCounts ModelCounts,
        Dictionary<string, SecretSlotReport> SlotReports,
        string CkTypeId,
        OctoObjectId RtId)
    {
        public SecretSlotReport GetSlot(string attributePath)
        {
            var key = CkTypeId + "|" + attributePath;
            if (!SlotReports.TryGetValue(key, out var report))
            {
                report = new SecretSlotReport(CkTypeId, attributePath, new SecretFormCounts());
                SlotReports[key] = report;
            }

            return report;
        }
    }
}
