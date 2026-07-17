using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     Default implementation of <see cref="ICkSemVerClassifier" />. <see cref="ClassifyChange" />
///     is the central rule table; every branch mirrors a row of the documented rule set in
///     <c>docs/ck-semver-rules.md</c> — keep both in sync when extending the rules.
/// </summary>
public class CkSemVerClassifier : ICkSemVerClassifier
{
    /// <inheritdoc />
    public IReadOnlyList<CkClassifiedModelChange> Classify(IReadOnlyList<CkModelChange> changes,
        CkCompiledModelRoot baseline, CkCompiledModelRoot current)
    {
        return changes.Select(change => ClassifyChange(change, current)).ToList();
    }

    /// <inheritdoc />
    public CkSemVerLevel GetRequiredLevel(IEnumerable<CkClassifiedModelChange> classifiedChanges)
    {
        return classifiedChanges.Select(c => c.Level).DefaultIfEmpty(CkSemVerLevel.None).Max();
    }

    /// <inheritdoc />
    public CkSemVerValidationResult ValidateDeclaredVersion(CkVersion publishedVersion, CkVersion declaredVersion,
        CkSemVerLevel requiredLevel)
    {
        var minimumVersion = publishedVersion.Bump(requiredLevel);

        CkSemVerVerdict verdict;
        if (declaredVersion.CompareTo(publishedVersion) < 0)
        {
            // A version below the published one is always invalid, independent of the diff.
            verdict = CkSemVerVerdict.Downgrade;
        }
        else if (requiredLevel == CkSemVerLevel.None)
        {
            verdict = declaredVersion == publishedVersion
                ? CkSemVerVerdict.Valid
                : CkSemVerVerdict.ValidBumpWithoutStructuralChange;
        }
        else
        {
            verdict = declaredVersion.CompareTo(minimumVersion) >= 0
                ? CkSemVerVerdict.Valid
                : CkSemVerVerdict.VersionTooLow;
        }

        return new CkSemVerValidationResult
        {
            Verdict = verdict,
            PublishedVersion = publishedVersion,
            DeclaredVersion = declaredVersion,
            RequiredLevel = requiredLevel,
            MinimumVersion = minimumVersion
        };
    }

    /// <summary>
    ///     The central rule table. Rules are grouped by element kind; unmatched changes fall
    ///     through to the defensive default (major).
    /// </summary>
    private static CkClassifiedModelChange ClassifyChange(CkModelChange change, CkCompiledModelRoot current)
    {
        var result = change switch
        {
            // ── Documentational changes (all element kinds, model meta) ─────────────────────
            { ChangeKind: CkModelChangeKind.Modified, Property: "description" } =>
                (CkSemVerLevel.Patch, "purely documentational change"),

            // ── Dependencies ────────────────────────────────────────────────────────────────
            { ElementKind: CkModelElementKind.Dependency } => ClassifyDependencyChange(change),

            // ── Element definitions: removal is always breaking, addition is additive ──────
            { ElementKind: CkModelElementKind.Type or CkModelElementKind.Attribute or CkModelElementKind.Enum
                or CkModelElementKind.Record or CkModelElementKind.AssociationRole,
                ChangeKind: CkModelChangeKind.Removed } =>
                (CkSemVerLevel.Major, "consumers reference the removed element"),
            { ElementKind: CkModelElementKind.Type or CkModelElementKind.Attribute or CkModelElementKind.Enum
                or CkModelElementKind.Record or CkModelElementKind.AssociationRole,
                ChangeKind: CkModelChangeKind.Added } =>
                (CkSemVerLevel.Minor, "purely additive element"),

            // ── Types and records ───────────────────────────────────────────────────────────
            { ElementKind: CkModelElementKind.Type, Property: "derivedFromCkTypeId" } =>
                (CkSemVerLevel.Major, "inheritance hierarchy breaks (GraphQL schema, queries)"),
            { ElementKind: CkModelElementKind.Record, Property: "derivedFromCkRecordId" } =>
                (CkSemVerLevel.Major, "inheritance hierarchy breaks (GraphQL schema, queries)"),
            { ElementKind: CkModelElementKind.Type or CkModelElementKind.Record, Property: "isAbstract" or "isFinal" } =>
                change.NewValue == "true"
                    ? (CkSemVerLevel.Major, "instantiation/derivation breaks")
                    : (CkSemVerLevel.Minor, "relaxation"),
            { ElementKind: CkModelElementKind.Type, Property: "isCollectionRoot" } =>
                change.NewValue == "true"
                    ? (CkSemVerLevel.Minor, "type becomes a collection root")
                    : (CkSemVerLevel.Major, "type is no longer a collection root"),
            { ElementKind: CkModelElementKind.Type, Property: "enableChangeStreamPreAndPostImages" } =>
                (CkSemVerLevel.Minor, "change stream behavior changes, no data break"),

            // ── Attribute definitions ───────────────────────────────────────────────────────
            { ElementKind: CkModelElementKind.Attribute, Property: "valueType" } =>
                (CkSemVerLevel.Major, "data format breaks"),
            { ElementKind: CkModelElementKind.Attribute, Property: "valueCkEnumId" or "valueCkRecordId" } =>
                (CkSemVerLevel.Major, "reference target breaks"),
            { ElementKind: CkModelElementKind.Attribute, Property: "defaultValues" } =>
                (CkSemVerLevel.Minor, "behavior of newly created instances changes"),
            { ElementKind: CkModelElementKind.Attribute, Property: "isRuntimeState" } =>
                (CkSemVerLevel.Minor, "blueprint re-apply behavior changes"),
            { ElementKind: CkModelElementKind.Attribute, Property: "metaData" } =>
                (CkSemVerLevel.Minor, "attribute metadata changes, no data break"),

            // ── Attribute assignments on types, records and association roles ──────────────
            { ElementKind: CkModelElementKind.TypeAttribute or CkModelElementKind.RecordAttribute
                or CkModelElementKind.AssociationRoleAttribute } =>
                ClassifyAttributeAssignmentChange(change, current),

            // ── Type associations ───────────────────────────────────────────────────────────
            { ElementKind: CkModelElementKind.TypeAssociation, ChangeKind: CkModelChangeKind.Added } =>
                (CkSemVerLevel.Minor, "purely additive association"),
            { ElementKind: CkModelElementKind.TypeAssociation, ChangeKind: CkModelChangeKind.Removed } =>
                (CkSemVerLevel.Major, "consumers use the association navigation"),
            { ElementKind: CkModelElementKind.TypeAssociation, Property: "targetCkAttributeIds" } =>
                (CkSemVerLevel.Major, "referential integrity attributes change (defensive)"),

            // ── Indexes ─────────────────────────────────────────────────────────────────────
            { ElementKind: CkModelElementKind.TypeIndex, ChangeKind: CkModelChangeKind.Added } =>
                IsUniqueIndex(change.NewValue)
                    ? (CkSemVerLevel.Major, "existing data may violate the new unique index")
                    : (CkSemVerLevel.Minor, "query behavior changes, no data break"),
            { ElementKind: CkModelElementKind.TypeIndex, ChangeKind: CkModelChangeKind.Removed } =>
                (CkSemVerLevel.Minor, "query behavior changes, no data break"),

            // ── Enums ───────────────────────────────────────────────────────────────────────
            { ElementKind: CkModelElementKind.Enum, Property: "useFlags" } =>
                (CkSemVerLevel.Major, "value semantics break"),
            { ElementKind: CkModelElementKind.Enum, Property: "isExtensible" } =>
                change.NewValue == "true"
                    ? (CkSemVerLevel.Minor, "relaxation, enum becomes extensible")
                    : (CkSemVerLevel.Major, "runtime extensions are no longer allowed"),
            { ElementKind: CkModelElementKind.EnumValue, ChangeKind: CkModelChangeKind.Added } =>
                (CkSemVerLevel.Minor, "purely additive enum value"),
            { ElementKind: CkModelElementKind.EnumValue, ChangeKind: CkModelChangeKind.Removed } =>
                (CkSemVerLevel.Major, "stored values become unreadable"),
            { ElementKind: CkModelElementKind.EnumValue, Property: "key" } =>
                (CkSemVerLevel.Major, "stored values become unreadable"),
            { ElementKind: CkModelElementKind.EnumValue, Property: "isExtension" } =>
                (CkSemVerLevel.Minor, "extension marker changes, no data break"),

            // ── Association roles ───────────────────────────────────────────────────────────
            { ElementKind: CkModelElementKind.AssociationRole, Property: "inboundName" or "outboundName" } =>
                (CkSemVerLevel.Major, "navigation/GraphQL breaks"),
            { ElementKind: CkModelElementKind.AssociationRole, Property: "inboundMultiplicity" or "outboundMultiplicity" } =>
                ClassifyMultiplicityChange(change),

            // ── Defensive default ───────────────────────────────────────────────────────────
            // Changes without an explicit rule are classified as major: only a minimum level is
            // enforced, so an overly strict classification is never wrong — an overly lax one is.
            _ => (CkSemVerLevel.Major, "no classification rule for this change — defensively classified as major")
        };

        return new CkClassifiedModelChange { Change = change, Level = result.Item1, Reason = result.Item2 };
    }

    private static (CkSemVerLevel, string) ClassifyDependencyChange(CkModelChange change)
    {
        switch (change.ChangeKind)
        {
            case CkModelChangeKind.Added:
                return (CkSemVerLevel.Minor, "purely additive dependency");
            case CkModelChangeKind.Removed:
                return (CkSemVerLevel.Major, "consumers may rely on the transitively provided model (defensive)");
            case CkModelChangeKind.Modified when change.Property == "version":
                var oldMajor = change.OldValue == null ? -1 : new CkVersion(change.OldValue).Major;
                var newMajor = change.NewValue == null ? -1 : new CkVersion(change.NewValue).Major;
                return oldMajor != newMajor
                    ? (CkSemVerLevel.Major, "dependency switched to a new major version — transitively breaking")
                    : (CkSemVerLevel.Minor, "compatible dependency version change");
            default:
                return (CkSemVerLevel.Major, "no classification rule for this dependency change — defensively classified as major");
        }
    }

    private static (CkSemVerLevel, string) ClassifyAttributeAssignmentChange(CkModelChange change,
        CkCompiledModelRoot current)
    {
        switch (change.ChangeKind)
        {
            case CkModelChangeKind.Removed:
                return (CkSemVerLevel.Major, "consumers reference the removed attribute");

            case CkModelChangeKind.Added:
                var assignment = FindAttributeAssignment(current, change.ElementKind, change.ElementId);
                if (assignment == null)
                {
                    return (CkSemVerLevel.Major, "added attribute could not be resolved — defensively classified as major");
                }

                if (assignment.IsOptional)
                {
                    return (CkSemVerLevel.Minor, "purely additive optional attribute");
                }

                // A new required attribute is only additive when existing instances can be
                // filled from default values of the referenced attribute definition. Attributes
                // defined in another model cannot be inspected here and are classified
                // defensively.
                var hasDefaultValues = HasDefaultValues(current, assignment.CkAttributeId);
                return hasDefaultValues switch
                {
                    true => (CkSemVerLevel.Minor, "additive required attribute, existing data can be filled from default values"),
                    false => (CkSemVerLevel.Major, "new required attribute without default values — existing instances become invalid"),
                    null => (CkSemVerLevel.Major, "new required attribute references an attribute of another model — defensively classified as major")
                };

            case CkModelChangeKind.Modified when change.Property == "isOptional":
                return change.NewValue == "false"
                    ? (CkSemVerLevel.Major, "attribute changed from optional to required — existing instances may become invalid")
                    : (CkSemVerLevel.Minor, "relaxation, attribute changed from required to optional");

            case CkModelChangeKind.Modified when change.Property == "id":
                return (CkSemVerLevel.Major, "attribute assignment references a different attribute definition (defensive)");

            case CkModelChangeKind.Modified when change.Property is "autoCompleteValues" or "autoIncrementReference":
                return (CkSemVerLevel.Minor, "behavior of newly created instances changes");

            default:
                return (CkSemVerLevel.Major, "no classification rule for this attribute assignment change — defensively classified as major");
        }
    }

    private static (CkSemVerLevel, string) ClassifyMultiplicityChange(CkModelChange change)
    {
        var oldRank = GetMultiplicityPermissiveness(change.OldValue);
        var newRank = GetMultiplicityPermissiveness(change.NewValue);
        if (oldRank == null || newRank == null)
        {
            return (CkSemVerLevel.Major, "unknown multiplicity value — defensively classified as major");
        }

        return newRank < oldRank
            ? (CkSemVerLevel.Major, "multiplicity tightened — existing associations may become invalid")
            : (CkSemVerLevel.Minor, "relaxation, multiplicity widened");
    }

    /// <summary>
    ///     Permissiveness rank of a multiplicity: One (exactly one) &lt; ZeroOrOne (optional
    ///     single) &lt; N (many). A decrease is a tightening (major), an increase a relaxation
    ///     (minor).
    /// </summary>
    private static int? GetMultiplicityPermissiveness(string? multiplicity)
    {
        return multiplicity switch
        {
            nameof(MultiplicitiesDto.One) => 0,
            nameof(MultiplicitiesDto.ZeroOrOne) => 1,
            nameof(MultiplicitiesDto.N) => 2,
            _ => null
        };
    }

    private static bool IsUniqueIndex(string? renderedIndex)
    {
        return renderedIndex != null &&
               (renderedIndex.StartsWith($"{nameof(IndexTypeDto.Unique)} ", StringComparison.Ordinal) ||
                renderedIndex.StartsWith($"{nameof(IndexTypeDto.UniqueNotDeleted)} ", StringComparison.Ordinal));
    }

    private static CkTypeAttributeDto? FindAttributeAssignment(CkCompiledModelRoot current,
        CkModelElementKind elementKind, string elementId)
    {
        // Element ids of attribute assignments have the shape "<ownerFullName>/<attributeName>"
        var separatorIndex = elementId.LastIndexOf('/');
        if (separatorIndex <= 0)
        {
            return null;
        }

        var ownerId = elementId.Substring(0, separatorIndex);
        var attributeName = elementId.Substring(separatorIndex + 1);

        var owner = elementKind switch
        {
            CkModelElementKind.TypeAttribute =>
                (CkTypeWithAttributesDto?)current.Types?.FirstOrDefault(t => t.TypeId.FullName == ownerId),
            CkModelElementKind.RecordAttribute =>
                current.Records?.FirstOrDefault(r => r.RecordId.FullName == ownerId),
            CkModelElementKind.AssociationRoleAttribute =>
                current.AssociationRoles?.FirstOrDefault(r => r.AssociationRoleId.FullName == ownerId),
            _ => null
        };

        return owner?.Attributes?.FirstOrDefault(a => a.AttributeName == attributeName);
    }

    /// <summary>
    ///     Returns whether the referenced attribute definition declares default values;
    ///     null when the reference points into another model and cannot be inspected here.
    /// </summary>
    private static bool? HasDefaultValues(CkCompiledModelRoot current, CkId<CkAttributeId> attributeReference)
    {
        if (attributeReference.ModelId.Name != current.ModelId.Name)
        {
            return null;
        }

        var attribute = current.Attributes?.FirstOrDefault(a =>
            a.AttributeId.FullName == attributeReference.ElementId.FullName);
        if (attribute == null)
        {
            return null;
        }

        return attribute.DefaultValues is { Count: > 0 };
    }
}
