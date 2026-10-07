using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.DisplayRules;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
///     Compile-time rules of the <see cref="AttributeValueTypesDto.Secret" /> value type (AB#5528,
///     <c>docs/concept-secret-attribute-type.md</c> §3.1, §3.2, §4.6). Runs on the model being
///     compiled only, after element, reference and inheritance resolution, so inherited attributes
///     and record keys are visible. Elements of dependency models were validated when those models
///     were compiled.
/// </summary>
/// <remarks>
///     Rules (message numbers in parentheses):
///     <list type="bullet">
///         <item>A Secret attribute definition has no <c>defaultValues</c> (70).</item>
///         <item>
///             A Secret attribute assignment has no <c>autoCompleteValues</c> and no
///             <c>autoIncrementReference</c>, and is not an association role attribute (71).
///         </item>
///         <item>
///             The effective ownership is always Secret: a declared ownership other than Secret on
///             the definition or an assignment is an error (72); an unset ownership becomes Secret.
///         </item>
///         <item>A Secret attribute is not part of an index (73) or a display rule (74).</item>
///         <item>
///             A model that uses Secret depends directly on System with a lower bound of at least
///             <see cref="SecretAttributeConventions.MinimumSystemVersion" /> (75, decision 12).
///         </item>
///         <item>
///             A record that contains a Secret sub-attribute (own or inherited) has a record key
///             (76); a declared record key names a required String/Int/Int64/Enum sub-attribute (77).
///         </item>
///     </list>
///     Owner attribute paths are covered by <c>InheritanceResolver.ValidateOwnerAttribute</c> (69),
///     query columns by <see cref="CkTypeQueryColumnCollector" />, which never emits a Secret column.
///     The CK model has no formula construct of its own (formulas are runtime archive columns), so
///     there is nothing to check for formulas at compile time.
/// </remarks>
internal static class SecretAttributeValidator
{
    /// <summary>
    ///     Validates the Secret rules for <paramref name="model" /> against the resolved
    ///     <paramref name="modelGraph" /> and normalises the ownership of Secret attribute definitions
    ///     to <see cref="AttributeOwnershipDto.Secret" /> in the model DTOs.
    /// </summary>
    /// <param name="model">The model being compiled (its DTOs carry variable-resolved ids)</param>
    /// <param name="dependencyRanges">The declared dependency ranges of the model</param>
    /// <param name="modelGraph">The resolved model graph including dependencies</param>
    /// <param name="originFileResolver">Resolves element ids to source files</param>
    /// <param name="operationResult">Receives the messages</param>
    /// <returns>True when the model uses the Secret value type</returns>
    public static bool Validate(CkModelRootBase model, IReadOnlyCollection<CkModelIdVersionRange>? dependencyRanges,
        CkModelGraph modelGraph, IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        var modelId = model.ModelId;
        var usesSecret = false;

        // ── Attribute definitions ───────────────────────────────────────────────────────────
        foreach (var attribute in model.Attributes ?? [])
        {
            if (attribute.ValueType != AttributeValueTypesDto.Secret)
            {
                continue;
            }

            usesSecret = true;
            var ckAttributeId = new CkId<CkAttributeId>(modelId, attribute.AttributeId);
            var location = originFileResolver.Resolve(ckAttributeId);

            if (attribute.DefaultValues is { Count: > 0 })
            {
                operationResult.AddMessage(MessageCodes.SecretAttributeHasDefaultValues(location, ckAttributeId));
            }

            if (attribute.Ownership.HasValue && attribute.Ownership.Value != AttributeOwnershipDto.Secret)
            {
                operationResult.AddMessage(MessageCodes.SecretAttributeOwnershipNotSecret(location, ckAttributeId,
                    attribute.AttributeId.Name, attribute.Ownership.Value));
            }

            // Effective ownership is Secret; making it explicit in the compiled model keeps the
            // exported definition honest for every reader (and its isRuntimeState mirror true).
            attribute.Ownership = AttributeOwnershipDto.Secret;
        }

        // ── Assignments on types, records and association roles ─────────────────────────────
        foreach (var type in model.Types ?? [])
        {
            var ckTypeId = new CkId<CkTypeId>(modelId, type.TypeId);
            usesSecret |= ValidateAssignments(ckTypeId.ToString(), originFileResolver.Resolve(ckTypeId),
                type.Attributes, false, modelGraph, operationResult);
        }

        foreach (var record in model.Records ?? [])
        {
            var ckRecordId = new CkId<CkRecordId>(modelId, record.RecordId);
            usesSecret |= ValidateAssignments(ckRecordId.ToString(), originFileResolver.Resolve(ckRecordId),
                record.Attributes, false, modelGraph, operationResult);
        }

        foreach (var associationRole in model.AssociationRoles ?? [])
        {
            var ckRoleId = new CkId<CkAssociationRoleId>(modelId, associationRole.AssociationRoleId);
            usesSecret |= ValidateAssignments(ckRoleId.ToString(), originFileResolver.Resolve(ckRoleId),
                associationRole.Attributes, true, modelGraph, operationResult);
        }

        // ── Indexes and display rules of own types ──────────────────────────────────────────
        foreach (var type in model.Types ?? [])
        {
            var ckTypeId = new CkId<CkTypeId>(modelId, type.TypeId);
            if (!modelGraph.Types.TryGetValue(ckTypeId, out var typeGraph))
            {
                continue;
            }

            var location = originFileResolver.Resolve(ckTypeId);
            foreach (var index in type.Indexes ?? [])
            {
                foreach (var field in index.Fields ?? [])
                {
                    foreach (var attributePath in field.AttributePaths ?? [])
                    {
                        if (PathTouchesSecret(modelGraph, typeGraph, attributePath))
                        {
                            operationResult.AddMessage(MessageCodes.SecretAttributeIndexed(location, ckTypeId,
                                attributePath));
                        }
                    }
                }
            }

            ValidateDisplayRule(modelGraph, typeGraph, ckTypeId, type.DisplayNameRule, "displayNameRule", location,
                operationResult);
            ValidateDisplayRule(modelGraph, typeGraph, ckTypeId, type.DisplayDescriptionRule,
                "displayDescriptionRule", location, operationResult);
        }

        // ── Records: record key ─────────────────────────────────────────────────────────────
        foreach (var record in model.Records ?? [])
        {
            var ckRecordId = new CkId<CkRecordId>(modelId, record.RecordId);
            if (!modelGraph.Records.TryGetValue(ckRecordId, out var recordGraph))
            {
                continue;
            }

            ValidateRecordKey(record, recordGraph, ckRecordId, originFileResolver.Resolve(ckRecordId),
                operationResult);
        }

        // ── System >= 2.5 gate (decision 12) ────────────────────────────────────────────────
        if (usesSecret && modelId.Name != SecretAttributeConventions.SystemModelName)
        {
            ValidateSystemDependency(modelId, dependencyRanges, originFileResolver, operationResult);
        }

        return usesSecret;
    }

    private static bool ValidateAssignments(string ckElementId, string location,
        IEnumerable<CkTypeAttributeDto>? assignments, bool isAssociationRole, CkModelGraph modelGraph,
        OperationResult operationResult)
    {
        var usesSecret = false;
        foreach (var assignment in assignments ?? [])
        {
            if (!modelGraph.Attributes.TryGetValue(assignment.CkAttributeId, out var attributeGraph) ||
                attributeGraph.ValueType != AttributeValueTypesDto.Secret)
            {
                continue;
            }

            usesSecret = true;

            if (isAssociationRole)
            {
                operationResult.AddMessage(MessageCodes.SecretAttributeAssignmentInvalid(location, ckElementId,
                    assignment.AttributeName, assignment.CkAttributeId,
                    "Secret attributes cannot be attributes of an association role"));
            }

            if (assignment.AutoCompleteValues is { Count: > 0 })
            {
                operationResult.AddMessage(MessageCodes.SecretAttributeAssignmentInvalid(location, ckElementId,
                    assignment.AttributeName, assignment.CkAttributeId,
                    "autoCompleteValues are not allowed - they would publish candidate secrets with the model"));
            }

            if (!string.IsNullOrWhiteSpace(assignment.AutoIncrementReference))
            {
                operationResult.AddMessage(MessageCodes.SecretAttributeAssignmentInvalid(location, ckElementId,
                    assignment.AttributeName, assignment.CkAttributeId,
                    "autoIncrementReference is not allowed - a generated sequence number is not a secret"));
            }

            if (assignment.Ownership.HasValue && assignment.Ownership.Value != AttributeOwnershipDto.Secret)
            {
                operationResult.AddMessage(MessageCodes.SecretAttributeOwnershipNotSecret(location, ckElementId,
                    assignment.AttributeName, assignment.Ownership.Value));
            }
        }

        return usesSecret;
    }

    private static void ValidateDisplayRule(CkModelGraph modelGraph, CkTypeGraph typeGraph,
        CkId<CkTypeId> ckTypeId, string? rule, string ruleProperty, string location, OperationResult operationResult)
    {
        if (string.IsNullOrWhiteSpace(rule))
        {
            return;
        }

        var parseResult = DisplayRuleParser.Parse(rule!);
        if (!parseResult.IsValid)
        {
            // Syntax errors are reported by InheritanceResolver.ValidateDisplayRules.
            return;
        }

        foreach (var attributePath in parseResult.ReferencedPaths)
        {
            if (PathTouchesSecret(modelGraph, typeGraph, attributePath))
            {
                operationResult.AddMessage(MessageCodes.SecretAttributeReferencedByDisplayRule(location,
                    ruleProperty, ckTypeId, attributePath));
            }
        }
    }

    private static void ValidateRecordKey(CkRecordDto record, CkRecordGraph recordGraph, CkId<CkRecordId> ckRecordId,
        string location, OperationResult operationResult)
    {
        if (!string.IsNullOrWhiteSpace(record.RecordKey))
        {
            var recordKey = record.RecordKey!.Trim();
            if (!recordGraph.AllAttributesByName.TryGetValue(recordKey, out var keyAttribute))
            {
                operationResult.AddMessage(MessageCodes.RecordKeyInvalid(location, ckRecordId, recordKey,
                    "the record has no attribute with this name"));
            }
            else if (keyAttribute.ValueType == AttributeValueTypesDto.Secret)
            {
                operationResult.AddMessage(MessageCodes.RecordKeyInvalid(location, ckRecordId, recordKey,
                    "a Secret attribute cannot be the record key"));
            }
            else if (!SecretAttributeConventions.AllowedRecordKeyValueTypes.Contains(keyAttribute.ValueType))
            {
                operationResult.AddMessage(MessageCodes.RecordKeyInvalid(location, ckRecordId, recordKey,
                    $"the key attribute must be of value type String, Int, Int64 or Enum, but is {keyAttribute.ValueType}"));
            }
            else if (keyAttribute.IsOptional)
            {
                operationResult.AddMessage(MessageCodes.RecordKeyInvalid(location, ckRecordId, recordKey,
                    "the key attribute must not be optional - an element without a key cannot be matched"));
            }
        }

        var secretAttributes = recordGraph.AllAttributes.Values
            .Where(a => a.ValueType == AttributeValueTypesDto.Secret)
            .Select(a => a.AttributeName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        if (secretAttributes.Count > 0 && string.IsNullOrWhiteSpace(recordGraph.RecordKey))
        {
            operationResult.AddMessage(MessageCodes.RecordWithSecretRequiresRecordKey(location, ckRecordId,
                string.Join(", ", secretAttributes.Select(n => $"'{n}'"))));
        }
    }

    private static void ValidateSystemDependency(CkModelId modelId,
        IReadOnlyCollection<CkModelIdVersionRange>? dependencyRanges, IOriginFileResolver originFileResolver,
        OperationResult operationResult)
    {
        var systemDependency = dependencyRanges?.FirstOrDefault(d =>
            string.Equals(d.Name, SecretAttributeConventions.SystemModelName, StringComparison.Ordinal));

        var minimumVersion = systemDependency?.ModelVersionRange.MinVersion;
        if (systemDependency != null && minimumVersion.HasValue &&
            minimumVersion.Value.CompareTo(SecretAttributeConventions.MinimumSystemVersion) >= 0)
        {
            return;
        }

        operationResult.AddMessage(MessageCodes.SecretAttributeRequiresSystemDependency(
            originFileResolver.Resolve(modelId.Name), modelId,
            SecretAttributeConventions.MinimumSystemVersion,
            systemDependency?.FullName ?? "no direct System dependency"));
    }

    /// <summary>
    ///     True when any segment of a dot-separated attribute path resolves to a Secret attribute.
    ///     Record segments are traversed; unknown segments (system attributes such as
    ///     <c>RtWellKnownName</c>, or paths other validators reject) end the walk without a match.
    /// </summary>
    private static bool PathTouchesSecret(CkModelGraph modelGraph, CkTypeWithAttributesGraph scope, string path)
    {
        var current = scope;
        foreach (var segment in path.Split('.'))
        {
            if (!current.AllAttributesByName.TryGetValue(segment, out var attribute))
            {
                return false;
            }

            if (attribute.ValueType == AttributeValueTypesDto.Secret)
            {
                return true;
            }

            if (attribute.ValueCkRecordId == null ||
                !modelGraph.Records.TryGetValue(attribute.ValueCkRecordId, out var recordGraph))
            {
                return false;
            }

            current = recordGraph;
        }

        return false;
    }
}
