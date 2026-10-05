using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Secrets;

namespace Meshmakers.Octo.Runtime.Engine.Secrets;

/// <summary>
///     Implementation of <see cref="ISecretWriteNormalizer" /> (AB#5532). Stateless apart from a
///     per-graph cache of "has Secret slots"; registered as a singleton. Never logs or echoes a value.
/// </summary>
public sealed class SecretWriteNormalizer : ISecretWriteNormalizer
{
    private readonly ISecretAttributeProtector _protector;
    private readonly ConditionalWeakTable<CkTypeWithAttributesGraph, StrongBox<bool>> _hasSecrets = new();

    /// <summary>
    ///     Creates a new instance.
    /// </summary>
    /// <param name="protector">Key ring access used to encrypt</param>
    public SecretWriteNormalizer(ISecretAttributeProtector protector)
    {
        _protector = protector;
    }

    /// <summary>
    ///     True when a value counts as "not set" for a Secret slot: <c>null</c>, <c>""</c>, a placeholder
    ///     (<see cref="SecretAttributeConventions.IsPlaceholder" />) - as a plain string or inside a
    ///     <see cref="RtSecretValueState.Pending" /> / <see cref="RtSecretValueState.LegacyPlaintext" />
    ///     value. The rule engine uses it for "creating requires a value for required secrets".
    /// </summary>
    /// <param name="value">Value of a Secret slot</param>
    /// <returns>True when the slot would hold no value after the write</returns>
    public static bool IsNotSetValue(object? value)
    {
        return value switch
        {
            null => true,
            string text => text.Length == 0 || SecretAttributeConventions.IsPlaceholder(text),
            RtSecretValue { IsProtected: true } => false,
            RtSecretValue secret => secret.RawValue.Length == 0 ||
                                    SecretAttributeConventions.IsPlaceholder(secret.RawValue),
            _ => false
        };
    }

    /// <summary>
    ///     True when a value of a Secret slot is a non-empty input value - neither <c>null</c>, <c>""</c>
    ///     nor a placeholder. Used to detect "set and cleared in the same operation".
    /// </summary>
    internal static bool IsNonEmptyValue(object? value)
    {
        return !IsNotSetValue(value);
    }

    /// <inheritdoc />
    public bool HasSecretAttributes(ICkCacheService ckCacheService, string tenantId,
        CkTypeWithAttributesGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (_hasSecrets.TryGetValue(graph, out var cached))
        {
            return cached.Value;
        }

        var result = ComputeHasSecrets(ckCacheService, tenantId, graph, []);
        _hasSecrets.AddOrUpdate(graph, new StrongBox<bool>(result));
        return result;
    }

    /// <inheritdoc />
    public bool AttributeHasSecrets(ICkCacheService ckCacheService, string tenantId, CkTypeAttributeGraph attribute)
    {
        ArgumentNullException.ThrowIfNull(attribute);
        return attribute.ValueType == AttributeValueTypesDto.Secret ||
               (attribute.ValueType is AttributeValueTypesDto.Record or AttributeValueTypesDto.RecordArray &&
                RecordAttributeHasSecrets(ckCacheService, tenantId, attribute));
    }

    /// <inheritdoc />
    public bool NeedsStoredEntity(ICkCacheService ckCacheService, string tenantId, CkTypeWithAttributesGraph graph,
        RtTypeWithAttributes incoming, SecretWriteOperation operation)
    {
        if (operation == SecretWriteOperation.Insert || !HasSecretAttributes(ckCacheService, tenantId, graph))
        {
            return false;
        }

        if (operation == SecretWriteOperation.Replace)
        {
            return true;
        }

        // Update: only record attributes carry over (a top-level "" is just left out of the write).
        foreach (var attribute in graph.AllAttributes.Values)
        {
            if (attribute.ValueType is AttributeValueTypesDto.Record or AttributeValueTypesDto.RecordArray &&
                incoming.Attributes.TryGetValue(attribute.AttributeName, out var value) && value != null &&
                RecordAttributeHasSecrets(ckCacheService, tenantId, attribute))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public SecretWriteResult Normalize(ICkCacheService ckCacheService, string tenantId,
        CkTypeWithAttributesGraph graph, RtTypeWithAttributes incoming, SecretWriteOperation operation,
        RtTypeWithAttributes? stored = null, SecretValueOrigin origin = SecretValueOrigin.Input)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(incoming);

        var result = new SecretWriteResult();
        if (!HasSecretAttributes(ckCacheService, tenantId, graph))
        {
            return result;
        }

        var context = new WriteContext(ckCacheService, tenantId, operation, origin, result);
        foreach (var attribute in graph.AllAttributes.Values)
        {
            if (attribute.ValueType == AttributeValueTypesDto.Secret)
            {
                NormalizeTopLevelSecret(context, attribute, incoming, stored);
            }
            else if (attribute.ValueType is AttributeValueTypesDto.Record or AttributeValueTypesDto.RecordArray &&
                     RecordAttributeHasSecrets(ckCacheService, tenantId, attribute))
            {
                NormalizeRecordAttribute(context, attribute, incoming, stored, attribute.AttributeName);
            }
        }

        return result;
    }

    /// <inheritdoc />
    public object? NormalizeAttributeValue(ICkCacheService ckCacheService, string tenantId,
        CkTypeAttributeGraph attribute, object? value, SecretValueOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(attribute);
        if (value == null)
        {
            return null;
        }

        var context = new WriteContext(ckCacheService, tenantId, SecretWriteOperation.Insert, origin,
            new SecretWriteResult());
        if (attribute.ValueType == AttributeValueTypesDto.Secret)
        {
            var secret = ToSecretValue(value, origin);
            return Classify(secret) == SlotKind.Value ? Protect(context, secret) : null;
        }

        if (attribute.ValueType is AttributeValueTypesDto.Record or AttributeValueTypesDto.RecordArray &&
            RecordAttributeHasSecrets(ckCacheService, tenantId, attribute))
        {
            // Run the record rules on a throw-away holder so the record objects are normalised in place.
            var holder = new RtRecord();
            holder.SetAttributeRawValue(attribute.AttributeName, value);
            NormalizeRecordAttribute(context, attribute, holder, null, attribute.AttributeName);
            return holder.Attributes.TryGetValue(attribute.AttributeName, out var normalised) ? normalised : null;
        }

        return value;
    }

    #region Top-level

    private void NormalizeTopLevelSecret(WriteContext context, CkTypeAttributeGraph attribute,
        RtTypeWithAttributes incoming, RtTypeWithAttributes? stored)
    {
        var name = attribute.AttributeName;
        var kind = ReadSlot(incoming, name, context.Origin, out var secret);
        switch (kind)
        {
            case SlotKind.Value:
                incoming.SetAttributeRawValue(name, Protect(context, secret!));
                break;
            case SlotKind.Placeholder:
                incoming.SetAttributeRawValue(name, null);
                break;
            case SlotKind.Null:
                // Explicit null clears (trusted callers, clear list mapped by the rule engine).
                break;
            case SlotKind.Empty:
                incoming.RemoveAttribute(name);
                if (context.Operation == SecretWriteOperation.Replace)
                {
                    CarryOver(context, name, incoming, stored);
                }

                break;
            case SlotKind.Absent:
                if (context.Operation == SecretWriteOperation.Replace)
                {
                    CarryOver(context, name, incoming, stored);
                }

                break;
        }

        if (!attribute.IsOptional)
        {
            // Update: only what is written can violate the rule (an explicit null / clear); insert and
            // replace: the whole entity is written, so the slot must end up with a value.
            var missing = context.Operation == SecretWriteOperation.Update
                ? incoming.Attributes.TryGetValue(name, out var updated) && updated == null
                : !HasValue(incoming, name);
            if (missing)
            {
                context.Result.AddMissing(name);
            }
        }
    }

    #endregion

    #region Records

    private void NormalizeRecordAttribute(WriteContext context, CkTypeAttributeGraph attribute,
        RtTypeWithAttributes incomingOwner, RtTypeWithAttributes? storedOwner, string path)
    {
        if (!incomingOwner.Attributes.TryGetValue(attribute.AttributeName, out var incomingValue) ||
            incomingValue == null)
        {
            // An omitted or null record attribute: nothing is written into it (update) or the whole
            // record goes (replace) - the secrets inside go with their element.
            return;
        }

        object? storedValue = null;
        storedOwner?.Attributes.TryGetValue(attribute.AttributeName, out storedValue);

        if (attribute.ValueType == AttributeValueTypesDto.Record)
        {
            if (incomingValue is not RtRecord incomingRecord)
            {
                return;
            }

            var recordGraph = ResolveRecord(context, incomingRecord, attribute);
            if (recordGraph == null || !HasSecretAttributes(context.CkCacheService, context.TenantId, recordGraph))
            {
                return;
            }

            // Single record: carried over by position (there is exactly one element).
            NormalizeRecord(context, recordGraph, incomingRecord, storedValue as RtRecord, path);
            return;
        }

        if (incomingValue is not IEnumerable incomingElements || incomingValue is string)
        {
            return;
        }

        var storedElements = storedValue is IEnumerable storedEnumerable and not string
            ? storedEnumerable.OfType<RtRecord>().ToList()
            : [];

        var index = 0;
        foreach (var element in incomingElements)
        {
            if (element is not RtRecord incomingRecord)
            {
                index++;
                continue;
            }

            var recordGraph = ResolveRecord(context, incomingRecord, attribute);
            if (recordGraph == null || !HasSecretAttributes(context.CkCacheService, context.TenantId, recordGraph))
            {
                index++;
                continue;
            }

            var counterpart = FindByRecordKey(recordGraph.RecordKey, incomingRecord, storedElements);
            NormalizeRecord(context, recordGraph, incomingRecord, counterpart,
                BuildElementPath(path, recordGraph.RecordKey, incomingRecord, index));
            index++;
        }
    }

    private void NormalizeRecord(WriteContext context, CkRecordGraph recordGraph, RtRecord incoming,
        RtRecord? stored, string path)
    {
        foreach (var attribute in recordGraph.AllAttributes.Values)
        {
            var attributePath = path + "." + attribute.AttributeName;
            if (attribute.ValueType == AttributeValueTypesDto.Secret)
            {
                NormalizeRecordSecret(context, attribute, incoming, stored, attributePath);
            }
            else if (attribute.ValueType is AttributeValueTypesDto.Record or AttributeValueTypesDto.RecordArray &&
                     RecordAttributeHasSecrets(context.CkCacheService, context.TenantId, attribute))
            {
                NormalizeRecordAttribute(context, attribute, incoming, stored, attributePath);
            }
        }
    }

    private void NormalizeRecordSecret(WriteContext context, CkTypeAttributeGraph attribute, RtRecord incoming,
        RtRecord? stored, string path)
    {
        var name = attribute.AttributeName;
        var kind = ReadSlot(incoming, name, context.Origin, out var secret);
        switch (kind)
        {
            case SlotKind.Value:
                incoming.SetAttributeRawValue(name, Protect(context, secret!));
                break;
            case SlotKind.Placeholder:
                // The explicit "not set" inside a record (null / "" mean "unchanged" there).
                incoming.SetAttributeRawValue(name, null);
                break;
            case SlotKind.Null:
            case SlotKind.Empty:
            case SlotKind.Absent:
                if (kind == SlotKind.Empty)
                {
                    incoming.RemoveAttribute(name);
                }

                CarryOver(context, name, incoming, stored);
                break;
        }

        if (!attribute.IsOptional && !HasValue(incoming, name))
        {
            context.Result.AddMissing(path);
        }
    }

    private CkRecordGraph? ResolveRecord(WriteContext context, RtRecord record, CkTypeAttributeGraph attribute)
    {
        // The element's own record id first: a record array may hold derived records.
        if (record.CkRecordId is { IsEmpty: false } &&
            context.CkCacheService.TryGetRtCkRecord(context.TenantId, record.CkRecordId, out var graph))
        {
            return graph;
        }

        if (attribute.ValueCkRecordId != null &&
            context.CkCacheService.TryGetCkRecord(context.TenantId, attribute.ValueCkRecordId, out var declared))
        {
            return declared;
        }

        return null;
    }

    /// <summary>
    ///     Finds the stored element with the same record key value. Without a declared key (only possible
    ///     for models compiled before AB#5531) there is no carry-over for record arrays.
    /// </summary>
    internal static RtRecord? FindByRecordKey(string? recordKey, RtRecord incoming, IReadOnlyList<RtRecord> stored)
    {
        if (recordKey == null || stored.Count == 0 ||
            !incoming.Attributes.TryGetValue(recordKey, out var key) || key == null)
        {
            return null;
        }

        foreach (var candidate in stored)
        {
            if (candidate.Attributes.TryGetValue(recordKey, out var candidateKey) && candidateKey != null &&
                RecordKeyEquals(key, candidateKey))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    ///     Record keys compare by value: numbers across CLR types (int / long / enum key), everything
    ///     else by its invariant text, ordinal.
    /// </summary>
    internal static bool RecordKeyEquals(object a, object b)
    {
        if (IsIntegral(a) && IsIntegral(b))
        {
            return Convert.ToDecimal(a, CultureInfo.InvariantCulture) == Convert.ToDecimal(b, CultureInfo.InvariantCulture);
        }

        return string.Equals(Convert.ToString(a, CultureInfo.InvariantCulture),
            Convert.ToString(b, CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Path of a record array element for reports and errors: <c>Name[Key=value]</c> when the record
    ///     declares a key, <c>Name[index]</c> otherwise. Record keys are identifiers, never secrets.
    /// </summary>
    internal static string BuildElementPath(string path, string? recordKey, RtRecord element, int index)
    {
        if (recordKey != null && element.Attributes.TryGetValue(recordKey, out var key) && key != null)
        {
            return $"{path}[{recordKey}={Convert.ToString(key, CultureInfo.InvariantCulture)}]";
        }

        return $"{path}[{index.ToString(CultureInfo.InvariantCulture)}]";
    }

    private static bool IsIntegral(object value) =>
        value is byte or sbyte or short or ushort or int or uint or long or ulong or Enum;

    #endregion

    #region Values

    private void CarryOver(WriteContext context, string name, RtTypeWithAttributes incoming,
        RtTypeWithAttributes? stored)
    {
        if (stored == null || !stored.Attributes.TryGetValue(name, out var storedValue) || storedValue == null)
        {
            return;
        }

        // A stored string is legacy - never "input" - so an enc:v1 value is decrypted, not wrapped.
        var secret = ToSecretValue(storedValue, SecretValueOrigin.Storage);
        if (Classify(secret) != SlotKind.Value)
        {
            // A stored placeholder / empty legacy string is "not set".
            incoming.SetAttributeRawValue(name, null);
            return;
        }

        incoming.SetAttributeRawValue(name, Protect(context, secret));
        context.Result.CarriedOverCount++;
    }

    private static SlotKind ReadSlot(RtTypeWithAttributes owner, string name, SecretValueOrigin origin,
        out RtSecretValue? secret)
    {
        secret = null;
        if (!owner.Attributes.TryGetValue(name, out var raw))
        {
            return SlotKind.Absent;
        }

        if (raw == null)
        {
            return SlotKind.Null;
        }

        secret = ToSecretValue(raw, origin);
        return Classify(secret);
    }

    private static bool HasValue(RtTypeWithAttributes owner, string name)
    {
        return owner.Attributes.TryGetValue(name, out var value) && value != null;
    }

    /// <summary>
    ///     Maps whatever sits in a Secret slot to an <see cref="RtSecretValue" />.
    /// </summary>
    internal static RtSecretValue ToSecretValue(object value, SecretValueOrigin origin)
    {
        return value switch
        {
            RtSecretValue secret => secret,
            string text => origin == SecretValueOrigin.Storage
                ? RtSecretValue.LegacyPlaintext(text)
                : RtSecretValue.Pending(text),
            _ => AttributeValueConverter.ConvertAttributeValue(AttributeValueTypesDto.Secret, value) as RtSecretValue
                 ?? throw new InvalidOperationException(
                     $"A value of type '{value.GetType().Name}' cannot be stored in a Secret attribute.")
        };
    }

    private static SlotKind Classify(RtSecretValue secret)
    {
        switch (secret.State)
        {
            case RtSecretValueState.Protected:
                return SlotKind.Value;
            case RtSecretValueState.Pending:
                if (secret.RawValue.Length == 0)
                {
                    return SlotKind.Empty;
                }

                return SecretAttributeConventions.IsPlaceholder(secret.RawValue) ? SlotKind.Placeholder : SlotKind.Value;
            default:
                // A stored empty string or placeholder is "not set", not "unchanged".
                return secret.RawValue.Length == 0 || SecretAttributeConventions.IsPlaceholder(secret.RawValue)
                    ? SlotKind.Placeholder
                    : SlotKind.Value;
        }
    }

    private RtSecretValue Protect(WriteContext context, RtSecretValue secret)
    {
        switch (secret.State)
        {
            case RtSecretValueState.Protected:
                return secret;
            case RtSecretValueState.Pending:
                context.Result.ProtectedCount++;
                return _protector.Protect(secret.RawValue);
            default:
                return ProtectLegacy(context, secret);
        }
    }

    private RtSecretValue ProtectLegacy(WriteContext context, RtSecretValue legacy)
    {
        if (SecretEnvelope.TryParse(legacy.RawValue, out var info))
        {
            if (info.Version == SecretEnvelope.CurrentVersion)
            {
                // An enc:v2 envelope that was stored as a string: it already is ciphertext.
                return RtSecretValue.Protected(legacy.RawValue);
            }

            if (!_protector.IsConfigured)
            {
                return legacy;
            }

            try
            {
                var reprotected = _protector.Reprotect(legacy);
                context.Result.ProtectedCount++;
                return reprotected;
            }
            catch (SecretEncryptionNotConfiguredException)
            {
                // Active key present, legacy v1 key missing: the value cannot be re-encrypted here; it
                // stays as it was stored and the encrypt sweep reports it.
                return legacy;
            }
        }

        if (!_protector.IsConfigured)
        {
            // Already stored as clear text: keeping it does not expose anything new; the encrypt sweep
            // (which needs keys anyway) converts it.
            return legacy;
        }

        context.Result.ProtectedCount++;
        return _protector.Protect(legacy.RawValue);
    }

    #endregion

    #region Has secrets

    private bool RecordAttributeHasSecrets(ICkCacheService ckCacheService, string tenantId,
        CkTypeAttributeGraph attribute)
    {
        return RecordAttributeHasSecrets(ckCacheService, tenantId, attribute, null);
    }

    private bool RecordAttributeHasSecrets(ICkCacheService ckCacheService, string tenantId,
        CkTypeAttributeGraph attribute, HashSet<string>? visiting)
    {
        if (attribute.ValueCkRecordId == null ||
            !ckCacheService.TryGetCkRecord(tenantId, attribute.ValueCkRecordId, out var recordGraph))
        {
            return false;
        }

        if (visiting == null)
        {
            if (HasSecretAttributes(ckCacheService, tenantId, recordGraph))
            {
                return true;
            }
        }
        else if (ComputeHasSecrets(ckCacheService, tenantId, recordGraph, visiting))
        {
            return true;
        }

        // A record array may hold derived records that add Secret attributes.
        foreach (var derivedId in recordGraph.GetAllDerivedRecords(false))
        {
            if (ckCacheService.TryGetCkRecord(tenantId, derivedId, out var derived) &&
                (visiting == null
                    ? HasSecretAttributes(ckCacheService, tenantId, derived)
                    : ComputeHasSecrets(ckCacheService, tenantId, derived, visiting)))
            {
                return true;
            }
        }

        return false;
    }

    private bool ComputeHasSecrets(ICkCacheService ckCacheService, string tenantId,
        CkTypeWithAttributesGraph graph, HashSet<string> visiting)
    {
        var graphKey = graph is CkRecordGraph record ? "R:" + record.CkRecordId : "T:" + graph.GetHashCode();
        if (!visiting.Add(graphKey))
        {
            // A record cycle (invalid model) - stop instead of recursing forever.
            return false;
        }

        try
        {
            foreach (var attribute in graph.AllAttributes.Values)
            {
                if (attribute.ValueType == AttributeValueTypesDto.Secret)
                {
                    return true;
                }

                if (attribute.ValueType is AttributeValueTypesDto.Record or AttributeValueTypesDto.RecordArray &&
                    RecordAttributeHasSecrets(ckCacheService, tenantId, attribute, visiting))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            visiting.Remove(graphKey);
        }
    }

    #endregion

    private enum SlotKind
    {
        Absent,
        Null,
        Empty,
        Placeholder,
        Value
    }

    private sealed record WriteContext(
        ICkCacheService CkCacheService,
        string TenantId,
        SecretWriteOperation Operation,
        SecretValueOrigin Origin,
        SecretWriteResult Result);
}
