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
///     <see cref="IRuntimeRepository.RewriteAttributeValueIfUnchangedForMigrationAsync" />. Logs names and counts,
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
    internal const string RtIdSortPath = SecretEntityScanner.RtIdSortPath;

    private readonly SecretEntityScanner _scanner = new(repositoryProvider, ckCacheService, writeNormalizer);

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

        if (mode == SecretSweepMode.CleanupUnreadable && !options.ConfirmCleanupUnreadable)
        {
            throw new InvalidOperationException(
                "The CleanupUnreadable sweep deletes every Secret value whose key id is not in the key ring; such " +
                "values become readable again once their key is added. It requires " +
                "SecretSweepOptions.ConfirmCleanupUnreadable = true.");
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
        var repository = await _scanner.GetRepositoryAsync(tenantId, cancellationToken).ConfigureAwait(false);

        var modeTag = normalizePlaceholdersOnly ? NormalizePlaceholdersModeTag : ModeTag(mode);
        var result = new SecretSweepResult(tenantId, mode);
        var countsPerModel = new Dictionary<string, SecretFormCounts>(StringComparer.Ordinal);
        var batchSize = options.BatchSize > 0 ? options.BatchSize : SecretSweepOptions.Default.BatchSize;

        var types = _scanner.GetSecretTypes(tenantId, options.CkModelName);

        logger.LogInformation("Secret sweep {Mode} of tenant {TenantId}: {TypeCount} CK type(s) with Secret attributes",
            modeTag, tenantId, types.Count);

        var session = await repository.GetSessionAsync().ConfigureAwait(false);
        var processed = new HashSet<OctoObjectId>();
        var slotReports = new Dictionary<string, SecretSlotReport>(StringComparer.Ordinal);

        foreach (var type in types)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.CkTypesScanned++;
            await foreach (var entities in _scanner.ReadPagesAsync(repository, session, type, batchSize,
                               true, cancellationToken).ConfigureAwait(false))
            {
                foreach (var entity in entities)
                {
                    // A query of a type may return entities of derived types as well; count each once.
                    if (!processed.Add(entity.RtId))
                    {
                        continue;
                    }

                    result.EntitiesScanned++;
                    // AB#5532/AB#5544: archived (deleted) entities are processed so nothing stays in clear
                    // text or unreadable at rest, but they are no re-entry tasks.
                    var archived = entity.RtState == RtState.Archived;
                    if (archived)
                    {
                        result.ArchivedEntitiesScanned++;
                    }

                    var entityGraph = _scanner.ResolveEntityGraph(tenantId, entity, type);
                    var model = entityGraph.CkTypeId.ModelId.Name;
                    if (!countsPerModel.TryGetValue(model, out var modelCounts))
                    {
                        modelCounts = new SecretFormCounts();
                        countsPerModel[model] = modelCounts;
                    }

                    var context = new SweepContext(tenantId, mode, normalizePlaceholdersOnly, result, modelCounts,
                        slotReports, entityGraph.CkTypeId.ToRtCkId().ToString(), entity.RtId, archived);
                    var rewritten = await ProcessEntityAsync(context, repository, session, entityGraph, entity)
                        .ConfigureAwait(false);
                    if (rewritten)
                    {
                        result.EntitiesRewritten++;
                    }
                }
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
            "Secret sweep {Mode} of tenant {TenantId} done: {Entities} entities ({Archived} archived, " +
            "{ArchivedUnreadable} unreadable value(s) of archived entities not listed), {Total} values " +
            "(not set {NotSet}, placeholder {Placeholder}, plaintext {Plaintext}, enc_v1 {EncV1}, enc_v2 {EncV2}, " +
            "unknown kid {UnknownKid}), {Rewritten} rewritten, {Unreadable} unreadable (kept), {Cleared} cleared, " +
            "{SkippedLegacyV1KeyMissing} enc_v1 kept by cleanup (legacy key missing), {PlaceholdersNormalized} placeholder(s) " +
            "normalised, {Skipped} skipped (modified concurrently), {Failed} failed",
            modeTag, tenantId, result.EntitiesScanned, result.ArchivedEntitiesScanned,
            result.ArchivedUnreadableValues, result.Totals.Total, result.Totals.NotSet,
            result.Totals.Placeholder, result.Totals.Plaintext, result.Totals.EncV1, result.Totals.EncV2,
            result.Totals.UnknownKeyId, result.ValuesRewritten, result.Unreadable.Count, result.Cleared.Count,
            result.SkippedLegacyV1KeyMissing, result.PlaceholdersNormalized,
            result.SkippedConcurrentlyModified, result.Totals.Failed);

        return result;
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
            // Re-entry, cleared and failure entries use the inventory's attribute path (camelCase,
            // "endpoints[key=prod].token") so a report entry links to its inventory row (handover §7, §9).
            var elementPath = SecretInventoryService.ToCamelCase(attribute.AttributeName);
            bool changed;
            object? newValue;
            object? expectedValue;
            var countsBefore = new RewriteCounts(context.Result);
            if (attribute.ValueType == AttributeValueTypesDto.Secret)
            {
                expectedValue = value;
                (changed, newValue) = ProcessSlot(context, attribute.AttributeName, elementPath, value);
            }
            else
            {
                // The record walk changes the read value in place; keep what was read for the
                // conditional rewrite.
                expectedValue = CopyAttributeValue(value);
                newValue = value;
                changed = ProcessRecordValue(context, attribute, value, attribute.AttributeName, elementPath);
            }

            if (!changed)
            {
                continue;
            }

            try
            {
                // Compare-and-swap: the value may have changed since the page was read (an API write, a
                // second sweep); writing a transformation of the old value would undo that change.
                var written = await repository.RewriteAttributeValueIfUnchangedForMigrationAsync(session,
                        entity.CkTypeId ?? entityGraph.CkTypeId.ToRtCkId(), entity.RtId, attribute.AttributeName,
                        expectedValue, newValue)
                    .ConfigureAwait(false);
                if (written)
                {
                    rewrittenAny = true;
                    continue;
                }

                // Not a failure: the current value is someone else's newer write; the next sweep sees it.
                countsBefore.Restore(context.Result);
                context.Result.SkippedConcurrentlyModified++;
                logger.LogInformation(
                    "Secret sweep skipped attribute {AttributeName} of {CkTypeId}@{RtId} (tenant {TenantId}): modified concurrently",
                    attribute.AttributeName, context.CkTypeId, context.RtId, context.TenantId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The rewrite did not happen: take back what was counted for it.
                var rewrittenHere = context.Result.ValuesRewritten - countsBefore.ValuesRewritten;
                countsBefore.Restore(context.Result);

                context.Result.Totals.AddFailure();
                context.ModelCounts.AddFailure();
                context.Result.Failures.Add(new SecretSweepFailure(context.CkTypeId, context.RtId,
                    elementPath,
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
                var graph = _scanner.ResolveRecord(context.TenantId, record, attribute);
                changed |= ProcessRecord(context, attribute, record, reportPath + "[]",
                    SecretInventoryService.BuildElementPath(elementPath, graph?.RecordKey, record, index), graph);
            }

            index++;
        }

        return changed;
    }

    private bool ProcessRecord(SweepContext context, CkTypeAttributeGraph attribute, RtRecord record,
        string reportPath, string elementPath, CkRecordGraph? graph = null)
    {
        graph ??= _scanner.ResolveRecord(context.TenantId, record, attribute);
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
            var memberElementPath = elementPath + "." + SecretInventoryService.ToCamelCase(member.AttributeName);
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

    /// <summary>
    ///     Classifies one Secret slot, counts it and decides the new value for the sweep mode.
    /// </summary>
    private (bool Changed, object? NewValue) ProcessSlot(SweepContext context, string reportPath, string elementPath,
        object? value)
    {
        var slot = context.GetSlot(reportPath);
        if (IsEnvelopeStoredAsLegacyString(value))
        {
            // AB#5532: an enc:v2 envelope in a string slot has no legitimate source (the engine stores
            // enc:v2 only as the protected sub-document); it was copied there. Reported, never decrypted,
            // re-protected or adopted as a protected value, and left as stored - in every mode.
            slot.Counts.AddFailure();
            context.Result.Totals.AddFailure();
            context.ModelCounts.AddFailure();
            context.Result.Failures.Add(new SecretSweepFailure(context.CkTypeId, context.RtId, elementPath,
                "An 'enc:v2' envelope is stored as a legacy string; it is not decrypted. Enter the secret again."));
            logger.LogWarning(
                "Secret sweep found an enc:v2 envelope stored as a legacy string in {AttributePath} of {CkTypeId}@{RtId} (tenant {TenantId}); left as stored",
                elementPath, context.CkTypeId, context.RtId, context.TenantId);
            return (false, null);
        }

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

        // A legacy enc:v1 string on a host without SecretEncryption:LegacyV1Key is a configuration gap of this
        // host (recoverable by configuring the legacy key), not key loss: even CleanupUnreadable keeps it.
        var legacyV1KeyMissing = form == SecretValueForm.UnknownKeyId && IsLegacyV1WithoutKey(value, keyId);
        if (form == SecretValueForm.UnknownKeyId && context.IsArchived)
        {
            // A deleted entity is no re-entry task: counted only (kept, or deleted by CleanupUnreadable).
            context.Result.ArchivedUnreadableValues++;
        }
        else if (form == SecretValueForm.UnknownKeyId &&
                 (context.Mode != SecretSweepMode.CleanupUnreadable || legacyV1KeyMissing))
        {
            // Decisions 2026-10-06, item 2: kept as stored and reported as a re-entry task.
            context.Result.Unreadable.Add(new SecretSweepUnreadableValue(context.CkTypeId, context.RtId, elementPath,
                keyId));
        }

        try
        {
            var (changed, newValue, effect) = Decide(context, form.Value, keyId, raw, value);
            if (changed)
            {
                context.Result.ValuesRewritten++;
                switch (effect)
                {
                    case SlotEffect.Cleared when !context.IsArchived:
                        // Only values deleted by CleanupUnreadable belong here (not those of deleted entities).
                        context.Result.Cleared.Add(new SecretSweepClearedValue(context.CkTypeId, context.RtId,
                            elementPath, form.Value, keyId));
                        break;
                    case SlotEffect.PlaceholderNormalized:
                        context.Result.PlaceholdersNormalized++;
                        break;
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

    private (bool Changed, object? NewValue, SlotEffect Effect) Decide(SweepContext context, SecretValueForm form,
        string? keyId, string? raw, object? value)
    {
        var access = new SecretAccessContext(context.TenantId, context.CkTypeId, null, "secret-sweep");
        if (context.NormalizePlaceholdersOnly)
        {
            return form == SecretValueForm.Placeholder
                ? (true, null, SlotEffect.PlaceholderNormalized)
                : (false, null, SlotEffect.None);
        }

        switch (context.Mode)
        {
            case SecretSweepMode.Verify:
                return (false, null, SlotEffect.None);

            case SecretSweepMode.Encrypt:
            case SecretSweepMode.Reprotect:
                switch (form)
                {
                    case SecretValueForm.Placeholder:
                        return (true, null, SlotEffect.PlaceholderNormalized);
                    case SecretValueForm.Plaintext:
                        // Through Reprotect: counted as a plaintext read and allowed in strict mode
                        // (the sweep is what converts the remaining clear text).
                        return (true, protector.Reprotect(RtSecretValue.LegacyPlaintext(raw!), access), SlotEffect.None);
                    case SecretValueForm.EncV1:
                        return (true, protector.Reprotect(RtSecretValue.LegacyPlaintext(raw!), access), SlotEffect.None);
                    case SecretValueForm.EncV2:
                    {
                        // Only protected values get here: an enc:v2 envelope stored as a legacy string is
                        // a failure (ProcessSlot), never adopted as a protected value (AB#5532).
                        var stored = (RtSecretValue)value!;
                        if (context.Mode == SecretSweepMode.Reprotect && protector.NeedsReprotect(stored))
                        {
                            return (true, protector.Reprotect(stored, access), SlotEffect.None);
                        }

                        return (false, null, SlotEffect.None);
                    }
                    default:
                        return (false, null, SlotEffect.None);
                }

            case SecretSweepMode.CleanupUnreadable:
                if (form != SecretValueForm.UnknownKeyId)
                {
                    return (false, null, SlotEffect.None);
                }

                if (IsLegacyV1WithoutKey(value, keyId))
                {
                    // PO decision (AB#5532): an enc:v1 value is unreadable here only because the legacy key is
                    // not configured - configuring it makes the value readable again, so it is never deleted.
                    // Kept in Unreadable (ProcessSlot) and counted separately.
                    context.Result.SkippedLegacyV1KeyMissing++;
                    return (false, null, SlotEffect.None);
                }

                // The only mode that deletes a value of an unknown key id (explicitly confirmed).
                return (true, null, SlotEffect.Cleared);

            case SecretSweepMode.Decrypt:
                switch (form)
                {
                    case SecretValueForm.EncV1:
                    case SecretValueForm.EncV2:
                        // EMERGENCY path: clear text goes back into a string slot (concept §5.2 rollback).
                        return (true, protector.Unprotect(raw!, access), SlotEffect.None);
                    case SecretValueForm.UnknownKeyId:
                        throw new UnknownSecretKeyIdException(keyId ?? string.Empty);
                    default:
                        return (false, null, SlotEffect.None);
                }

            default:
                return (false, null, SlotEffect.None);
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

        // Migration only: a legacy string that is exactly a placeholder (or empty) is "not set".
        if (raw.Length == 0 || SecretAttributeConventions.IsLegacyPlaceholder(raw))
        {
            return (SecretValueForm.Placeholder, null, raw);
        }

        if (SecretEnvelope.TryParse(raw, out var info))
        {
            if (info.Version == 1)
            {
                // AB#5532: without the legacy key an enc:v1 string cannot be read on this host - it is
                // classified like an unknown key id (kept, listed as a re-entry task, key id "enc:v1").
                return protector.IsLegacyV1KeyConfigured
                    ? (SecretValueForm.EncV1, null, raw)
                    : (SecretValueForm.UnknownKeyId, SecretValueStates.LegacyV1KeyId, raw);
            }

            return (protector.IsKnownKeyId(info.KeyId) ? SecretValueForm.EncV2 : SecretValueForm.UnknownKeyId,
                info.KeyId, raw);
        }

        return (SecretValueForm.Plaintext, null, raw);
    }

    /// <summary>
    ///     True for a legacy <c>enc:v1</c> string that <see cref="Classify" /> reported as
    ///     <see cref="SecretValueForm.UnknownKeyId" /> because the legacy key is not configured on this host
    ///     (key id <see cref="SecretValueStates.LegacyV1KeyId" />, never a protected value).
    /// </summary>
    private static bool IsLegacyV1WithoutKey(object? value, string? keyId)
    {
        return value is not RtSecretValue { IsProtected: true } &&
               string.Equals(keyId, SecretValueStates.LegacyV1KeyId, StringComparison.Ordinal);
    }

    /// <summary>
    ///     True for a string (or a non-protected <see cref="RtSecretValue" />) in a Secret slot whose text is
    ///     a structurally valid <c>enc:v2</c> envelope (any key id).
    /// </summary>
    internal static bool IsEnvelopeStoredAsLegacyString(object? value)
    {
        var text = value switch
        {
            string s => s,
            RtSecretValue { IsProtected: false } secret => secret.RawValue,
            _ => null
        };

        return text != null && SecretEnvelope.TryParse(text, out var info) && info.Version != 1;
    }

    private static string ModeTag(SecretSweepMode mode)
    {
        return mode switch
        {
            SecretSweepMode.Verify => "verify",
            SecretSweepMode.Encrypt => "encrypt",
            SecretSweepMode.Reprotect => "reprotect",
            SecretSweepMode.CleanupUnreadable => "cleanup_unreadable",
            SecretSweepMode.Decrypt => "decrypt",
            _ => mode.ToString()
        };
    }

    /// <summary>
    ///     Deep copy of an attribute value as read: records and arrays are copied (the record walk changes
    ///     them in place), scalars and <see cref="RtSecretValue" />s are immutable and shared.
    /// </summary>
    internal static object? CopyAttributeValue(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case RtRecord record:
                return new RtRecord(record.CkRecordId,
                    record.Attributes.ToDictionary(p => p.Key, p => CopyAttributeValue(p.Value), StringComparer.Ordinal));
            case string or byte[] or IDictionary:
                return value;
            case IEnumerable<RtRecord> records:
                return records.Select(r => (RtRecord)CopyAttributeValue(r)!).ToList();
            case IEnumerable elements:
                return elements.Cast<object?>().Select(CopyAttributeValue).ToList();
            default:
                return value;
        }
    }

    private enum SlotEffect
    {
        None,
        Cleared,
        PlaceholderNormalized
    }

    /// <summary>
    ///     The result counters an attribute rewrite adds to, taken before the attribute is processed so a
    ///     rewrite that does not happen (failure, concurrent modification) can take its counts back.
    /// </summary>
    private readonly struct RewriteCounts(SecretSweepResult result)
    {
        public long ValuesRewritten { get; } = result.ValuesRewritten;

        private int Cleared { get; } = result.Cleared.Count;

        private long PlaceholdersNormalized { get; } = result.PlaceholdersNormalized;

        public void Restore(SecretSweepResult target)
        {
            target.ValuesRewritten = ValuesRewritten;
            target.PlaceholdersNormalized = PlaceholdersNormalized;
            if (target.Cleared.Count > Cleared)
            {
                // The deletion did not happen: the values are still stored and unreadable.
                foreach (var notCleared in target.Cleared.Skip(Cleared))
                {
                    if (notCleared.PreviousForm == SecretValueForm.UnknownKeyId)
                    {
                        target.Unreadable.Add(new SecretSweepUnreadableValue(notCleared.CkTypeId, notCleared.RtId,
                            notCleared.AttributePath, notCleared.KeyId));
                    }
                }

                target.Cleared.RemoveRange(Cleared, target.Cleared.Count - Cleared);
            }
        }
    }

    private sealed record SweepContext(
        string TenantId,
        SecretSweepMode Mode,
        bool NormalizePlaceholdersOnly,
        SecretSweepResult Result,
        SecretFormCounts ModelCounts,
        Dictionary<string, SecretSlotReport> SlotReports,
        string CkTypeId,
        OctoObjectId RtId,
        bool IsArchived)
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
