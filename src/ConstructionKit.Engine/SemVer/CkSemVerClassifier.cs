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
    /// <summary>
    ///     Start of the reason of every defensive default ("no classification rule for this change / dependency
    ///     change / attribute assignment change"). The classification guard test (AB#6272) uses it to detect a
    ///     change that has no explicit rule.
    /// </summary>
    internal const string DefensiveDefaultReasonPrefix = "no classification rule for this ";

    /// <summary>
    ///     True when <paramref name="classifiedChange" /> was classified by a defensive default, i.e. no explicit
    ///     rule matched it (AB#6272).
    /// </summary>
    internal static bool IsDefensiveDefault(CkClassifiedModelChange classifiedChange) =>
        classifiedChange.Reason.StartsWith(DefensiveDefaultReasonPrefix, StringComparison.Ordinal);

    /// <inheritdoc />
    public IReadOnlyList<CkClassifiedModelChange> Classify(IReadOnlyList<CkModelChange> changes,
        CkCompiledModelRoot baseline, CkCompiledModelRoot current)
    {
        // AB#6266: internal elements and members of internal owners are not part of the compatibility surface.
        var baselineVisibility = new CkVisibilityIndex(baseline);
        var currentVisibility = new CkVisibilityIndex(current);
        return changes
            .Select(change => CapInternal(Mark(ClassifyChange(change, baseline, current), baseline, current),
                baselineVisibility, currentVisibility))
            .ToList();
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
    private static int ParseCkLanguage(string? value) =>
        int.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var language) ? language : 1;

    /// <summary>
    ///     Reason of every change capped by rows N1/N2 (AB#6266).
    /// </summary>
    internal const string InternalReason = "internal element, not part of the compatibility surface";

    /// <summary>
    ///     Rows N1/N2 (AB#6266): a change of an element that is internal — itself or through its owner — in every
    ///     version where it exists is at most Minor (description-only stays Patch). Minor rather than None: a
    ///     structural change still needs a bump to reach tenants (a same-version re-import is short-circuited).
    ///     Rows N3–N5 follow from "every version": an element public in the baseline (removed, made internal) or made
    ///     public in this release is classified by the public rules.
    /// </summary>
    private static CkClassifiedModelChange CapInternal(CkClassifiedModelChange classified, CkVisibilityIndex baseline,
        CkVisibilityIndex current)
    {
        var change = classified.Change;
        var inBaseline = baseline.IsInternal(change.ElementKind, change.ElementId);
        var inCurrent = current.IsInternal(change.ElementKind, change.ElementId);
        var isInternal = change.ChangeKind switch
        {
            CkModelChangeKind.Added => inCurrent == true && inBaseline != false,
            CkModelChangeKind.Removed => inBaseline == true && inCurrent != false,
            _ => inBaseline == true && inCurrent == true
        };
        if (!isInternal || classified.Level == CkSemVerLevel.None)
        {
            return classified;
        }

        var level = classified.Level > CkSemVerLevel.Minor ? CkSemVerLevel.Minor : classified.Level;
        return classified with { Level = level, Reason = $"{InternalReason} (public rule: {classified.Reason})" };
    }

    private static CkClassifiedModelChange ClassifyChange(CkModelChange change, CkCompiledModelRoot baseline,
        CkCompiledModelRoot current)
    {
        var result = change switch
        {
            // ── Documentational changes (all element kinds, model meta) ─────────────────────
            { ChangeKind: CkModelChangeKind.Modified, Property: "description" or "documentation" } =>
                (CkSemVerLevel.Patch, "purely documentational change"),

            // ── Dependencies ────────────────────────────────────────────────────────────────
            { ElementKind: CkModelElementKind.Dependency } => ClassifyDependencyChange(change),

            // ── CK v2 range retention (AB#6271, rows D1–D6) ─────────────────────────────────
            { ElementKind: CkModelElementKind.Model, Property: "rangeRetention" } =>
                (CkSemVerLevel.Minor,
                    "dependency pinning switched between exact pins and range retention — the one-time re-pin (F2.6); " +
                    "the resolved dependencies are still compared"),
            { ElementKind: CkModelElementKind.DependencyRange } => ClassifyDependencyRangeChange(change),

            // ── CK v2 (AB#5584) ──────────────────────────────────────────────────────
            { ElementKind: CkModelElementKind.Model, Property: "ckLanguage" } =>
                // Review L16: compared numerically ("10" > "2"), not ordinally.
                ParseCkLanguage(change.NewValue) > ParseCkLanguage(change.OldValue)
                    ? (CkSemVerLevel.Minor, "CK language version raised; older engines reject the model with a clear error")
                    : (CkSemVerLevel.Major, "CK language version lowered — CK v2 elements may disappear (defensive)"),
            // ── CK v2 modifiers (F1.1-S4, concept §4.3.2) ───────────────────────────────────────
            { ChangeKind: CkModelChangeKind.Modified, Property: "visibility" } =>
                change.NewValue == nameof(CkVisibilityDto.Internal)
                    ? (CkSemVerLevel.Major, "element made internal — other models can no longer reference it")
                    : (CkSemVerLevel.Minor, "element made public"),
            { ChangeKind: CkModelChangeKind.Modified, Property: "derivable" } =>
                change.NewValue == nameof(CkDerivableDto.Model)
                    ? (CkSemVerLevel.Major, "derivation restricted to the declaring model — derived types in other models break")
                    : (CkSemVerLevel.Minor, "derivation opened to other models"),
            { ElementKind: CkModelElementKind.Interface, ChangeKind: CkModelChangeKind.Added } =>
                (CkSemVerLevel.Minor, "purely additive interface"),
            { ElementKind: CkModelElementKind.Interface, ChangeKind: CkModelChangeKind.Removed } =>
                (CkSemVerLevel.Major, "implementing types and interface consumers break"),
            // AB#6267 rows I1–I7: interface members grow additively within the element version
            { ElementKind: CkModelElementKind.InterfaceAttribute or CkModelElementKind.InterfaceAssociation } =>
                ClassifyInterfaceMemberChange(change, current),
            // ── CK v2 interface completion (F1.1-S5) ────────────────────────────────────────────
            { ElementKind: CkModelElementKind.Interface, Property: "deprecated" } =>
                change.NewValue == "true"
                    ? (CkSemVerLevel.Minor, "interface deprecated — dependents get a compile warning, nothing breaks")
                    : (CkSemVerLevel.Minor, "interface deprecation withdrawn"),
            { ElementKind: CkModelElementKind.InterfaceExtends } =>
                (CkSemVerLevel.Major,
                    $"interface '{OwnerOf(change.ElementId)}' extends changed — implementors gain or lose required members; " +
                    "publish a new interface version instead (row I8)"),
            { ElementKind: CkModelElementKind.InterfaceMethod, ChangeKind: CkModelChangeKind.Added } =>
                (CkSemVerLevel.Major,
                    "interface method added — implementors must provide it; an optional interface method does not exist " +
                    "yet, so publish a new interface version instead (row I11)"),
            { ElementKind: CkModelElementKind.InterfaceMethod, ChangeKind: CkModelChangeKind.Removed } =>
                (CkSemVerLevel.Major, "interface method removed — callers through the interface break (row M1)"),
            // ── CK v2 methods (AB#6268, rows M1–M14), type and interface methods alike ──────────
            { ElementKind: CkModelElementKind.TypeMethod or CkModelElementKind.InterfaceMethod, Property: "signature" } =>
                (CkSemVerLevel.None, "signature summary — the level comes from the method field changes"),
            { ElementKind: CkModelElementKind.TypeMethod or CkModelElementKind.InterfaceMethod,
                ChangeKind: CkModelChangeKind.Modified } => ClassifyMethodChange(change),
            { ElementKind: CkModelElementKind.MethodParameter } => ClassifyMethodParameterChange(change, current),
            { ElementKind: CkModelElementKind.MethodError, ChangeKind: CkModelChangeKind.Removed } =>
                (CkSemVerLevel.Major, "error code removed — callers handling it break (row M7)"),
            { ElementKind: CkModelElementKind.MethodError, ChangeKind: CkModelChangeKind.Added } =>
                (CkSemVerLevel.Major,
                    "error code added — callers do not expect it; 'errors: open' does not exist yet (row M8)"),
            { ElementKind: CkModelElementKind.TypeAssociation, Property: "targetCkInterfaceId" } =>
                change.NewValue == null
                    ? (CkSemVerLevel.Minor, "association target no longer narrowed to an interface")
                    : (CkSemVerLevel.Major, "association target narrowed to interface implementors — existing associations may become invalid"),
            { ElementKind: CkModelElementKind.TypeInterface, ChangeKind: CkModelChangeKind.Added } =>
                (CkSemVerLevel.Minor, "type implements an additional interface"),
            { ElementKind: CkModelElementKind.TypeInterface, ChangeKind: CkModelChangeKind.Removed } =>
                (CkSemVerLevel.Major, "consumers querying the type through the interface break"),
            { ElementKind: CkModelElementKind.TypeMethod, ChangeKind: CkModelChangeKind.Added } =>
                (CkSemVerLevel.Minor, "purely additive method"),
            { ElementKind: CkModelElementKind.TypeMethod, ChangeKind: CkModelChangeKind.Removed } =>
                (CkSemVerLevel.Major, "callers of the removed method break"),

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
            // AB#5528: the record key only decides how secret sub-values are carried over when a
            // record array is replaced; no stored data or schema shape changes.
            { ElementKind: CkModelElementKind.Record, Property: "recordKey" } =>
                (CkSemVerLevel.Minor, "record element identity for secret carry-over changes, no data break"),
            { ElementKind: CkModelElementKind.Type, Property: "isCollectionRoot" } =>
                change.NewValue == "true"
                    ? (CkSemVerLevel.Minor, "type becomes a collection root")
                    : (CkSemVerLevel.Major, "type is no longer a collection root"),
            { ElementKind: CkModelElementKind.Type, Property: "enableChangeStreamPreAndPostImages" } =>
                (CkSemVerLevel.Minor, "change stream behavior changes, no data break"),
            { ElementKind: CkModelElementKind.Type, Property: "displayNameRule" or "displayDescriptionRule" } =>
                (CkSemVerLevel.Patch, "computed display values change only, no data/schema break"),
            { ElementKind: CkModelElementKind.Type, Property: "ownerAttributePath" } =>
                (CkSemVerLevel.Minor, "ownership semantics for owned-only data permissions change"),

            // ── Attribute definitions ───────────────────────────────────────────────────────
            // AB#5528 decision 2: String -> Secret keeps stored data readable - readers accept the
            // legacy plaintext during the transition and the sweep encrypts it afterwards - so the
            // conversion of a credential attribute is Minor. Every other value-type change,
            // including Secret -> String, stays Major.
            { ElementKind: CkModelElementKind.Attribute, Property: "valueType",
                OldValue: nameof(AttributeValueTypesDto.String), NewValue: nameof(AttributeValueTypesDto.Secret) } =>
                (CkSemVerLevel.Minor,
                    "String -> Secret: stored values stay readable (legacy plaintext is accepted until the sweep encrypts it); clients that select the value must switch to the is-set state"),
            { ElementKind: CkModelElementKind.Attribute, Property: "valueType" } =>
                (CkSemVerLevel.Major, "data format breaks"),
            { ElementKind: CkModelElementKind.Attribute, Property: "valueCkEnumId" or "valueCkRecordId" } =>
                (CkSemVerLevel.Major, "reference target breaks"),
            { ElementKind: CkModelElementKind.Attribute, Property: "defaultValues" } =>
                (CkSemVerLevel.Minor, "behavior of newly created instances changes"),
            { ElementKind: CkModelElementKind.Attribute, Property: "isRuntimeState" } =>
                (CkSemVerLevel.Minor, "blueprint re-apply behavior changes"),
            // AB#5187: ownership changes who wins on re-apply and whether the value is carried in
            // an ExportRt. Neither is a data-shape change, so it stays Minor — the same level the
            // boolean it replaces has always had. It still needs a bump: ImportCkModelAsync
            // short-circuits a same-version re-import, so a marker changed in place would reach no
            // existing tenant.
            { ElementKind: CkModelElementKind.Attribute, Property: "ownership" } =>
                (CkSemVerLevel.Minor, "blueprint re-apply and export behavior change"),
            { ElementKind: CkModelElementKind.Attribute, Property: "metaData" } =>
                (CkSemVerLevel.Minor, "attribute metadata changes, no data break"),
            // AB#6269 row A3: the marker alone changes no data and no API; it only decides how a later access
            // tightening is classified.
            { ElementKind: CkModelElementKind.Attribute, Property: "securitySensitive" } =>
                (CkSemVerLevel.Minor, "security-sensitivity marker set or cleared, no data or API change (row A3)"),

            // ── Attribute assignments on types, records and association roles ──────────────
            { ElementKind: CkModelElementKind.TypeAttribute or CkModelElementKind.RecordAttribute
                or CkModelElementKind.AssociationRoleAttribute } =>
                ClassifyAttributeAssignmentChange(change, baseline, current),

            // ── Type associations ───────────────────────────────────────────────────────────
            { ElementKind: CkModelElementKind.TypeAssociation, ChangeKind: CkModelChangeKind.Added } =>
                ClassifyTypeAssociationAdded(change, current),
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
            _ => (CkSemVerLevel.Major, DefensiveDefaultReasonPrefix + "change — defensively classified as major")
        };

        return new CkClassifiedModelChange { Change = change, Level = result.Item1, Reason = result.Item2 };
    }

    /// <summary>
    ///     Owner part of a member element id (<c>&lt;owner&gt;/&lt;member&gt;</c>).
    /// </summary>
    private static string OwnerOf(string elementId)
    {
        var separator = elementId.IndexOf('/');
        return separator < 0 ? elementId : elementId.Substring(0, separator);
    }

    /// <summary>
    ///     AB#6267 rows I1–I7 for attribute and association members of an interface.
    /// </summary>
    private static (CkSemVerLevel, string) ClassifyInterfaceMemberChange(CkModelChange change,
        CkCompiledModelRoot current)
    {
        var member = change.ElementKind == CkModelElementKind.InterfaceAttribute ? "attribute" : "association";
        var interfaceId = OwnerOf(change.ElementId);
        const string publish = "publish a new interface version (e.g. Named-2) instead";
        switch (change.ChangeKind)
        {
            case CkModelChangeKind.Added:
                return IsOptionalMember(change, current) switch
                {
                    true => (CkSemVerLevel.Minor,
                        $"optional {member} added to interface '{interfaceId}' — implementors need not provide it (row I1/I2)"),
                    false => (CkSemVerLevel.Major,
                        $"required {member} added to interface '{interfaceId}' — every implementor must provide it; {publish} (row I3)"),
                    null => (CkSemVerLevel.Major,
                        $"{member} added to interface '{interfaceId}' could not be resolved — defensively classified as major (row I3)")
                };
            case CkModelChangeKind.Removed:
                return (CkSemVerLevel.Major,
                    $"{member} removed from interface '{interfaceId}' (or renamed) — consumers break; {publish} (row I4)");
            case CkModelChangeKind.Modified when change.Property == "isOptional":
                return change.NewValue == "false"
                    ? (CkSemVerLevel.Major,
                        $"{member} of interface '{interfaceId}' made required — implementors may lack it; {publish} (row I7)")
                    : (CkSemVerLevel.Minor, $"{member} of interface '{interfaceId}' made optional — relaxation (row I7)");
            case CkModelChangeKind.Modified when change.Property == "id":
                return (CkSemVerLevel.Major,
                    $"attribute of interface '{interfaceId}' now references another attribute definition (value type, " +
                    $"record or enum change); {publish} (row I5)");
            case CkModelChangeKind.Modified when change.Property is "target" or "multiplicity":
                return (CkSemVerLevel.Major,
                    $"association of interface '{interfaceId}' changed its {change.Property} — implementors and " +
                    $"navigations break; {publish} (row I6)");
            default:
                return (CkSemVerLevel.Major,
                    DefensiveDefaultReasonPrefix + "interface member change — defensively classified as major");
        }
    }

    /// <summary>
    ///     Optionality of an added interface member: attributes are looked up in the current model, association members
    ///     carry it in their rendered value (<c>&lt;target&gt;, optional|required</c>); null when not resolvable.
    /// </summary>
    private static bool? IsOptionalMember(CkModelChange change, CkCompiledModelRoot current)
    {
        if (change.ElementKind == CkModelElementKind.InterfaceAttribute)
        {
            var separator = change.ElementId.LastIndexOf('/');
            var ckInterface = current.Interfaces?.FirstOrDefault(i =>
                i.InterfaceId.FullName == change.ElementId.Substring(0, Math.Max(separator, 0)));
            var attribute = ckInterface?.Attributes.FirstOrDefault(a => a.AttributeName == change.ElementId.Substring(separator + 1));
            if (attribute != null)
            {
                return attribute.IsOptional;
            }
        }

        return change.NewValue switch
        {
            { } value when value.EndsWith(", optional", StringComparison.Ordinal) => true,
            { } value when value.EndsWith(", required", StringComparison.Ordinal) => false,
            _ => null
        };
    }

    /// <summary>
    ///     AB#6268 rows M6, M9–M12: method-level field changes (type and interface methods).
    /// </summary>
    private static (CkSemVerLevel, string) ClassifyMethodChange(CkModelChange change)
    {
        switch (change.Property)
        {
            case "kind":
                return (CkSemVerLevel.Major, "method kind changed (static ↔ instance) — every call breaks (row M9)");
            case "result":
                return (CkSemVerLevel.Major,
                    "method result changed — callers read another value; widen a result record by an optional attribute " +
                    "instead (row M6)");
            case "idempotent":
                return change.NewValue == "false"
                    ? (CkSemVerLevel.Major, "method no longer idempotent — callers that retry break (row M10)")
                    : (CkSemVerLevel.Minor, "method declared idempotent — relaxation (row M10)");
            case "timeoutSeconds":
                return (CkSemVerLevel.Minor, "method timeout changed — behavioural change, no contract break (row M11)");
            case "authorization":
                return change.NewValue == "none"
                    ? (CkSemVerLevel.Minor, "method authorization removed — looser (row M12)")
                    : (CkSemVerLevel.Major, "method authorization added — callers may lose access (row M12)");
            case "roles" or "scopes":
                // Assumption (no gateway yet, Phase 3): roles and scopes are any-of. Removing an entry is stricter,
                // adding one looser; a mix of both is stricter.
                var before = SplitNames(change.OldValue);
                var after = SplitNames(change.NewValue);
                return before.Except(after).Any()
                    ? (CkSemVerLevel.Major, $"method authorization {change.Property} narrowed — callers may lose access (row M12)")
                    : (CkSemVerLevel.Minor, $"method authorization {change.Property} widened — looser (row M12)");
            case "allowSelf":
                return change.NewValue == "false"
                    ? (CkSemVerLevel.Major, "method no longer callable on the caller's own entity — stricter (row M12)")
                    : (CkSemVerLevel.Minor, "method callable on the caller's own entity — looser (row M12)");
            default:
                return (CkSemVerLevel.Major, DefensiveDefaultReasonPrefix + "method change — defensively classified as major");
        }
    }

    private static HashSet<string> SplitNames(string? value) =>
        value == null || value.Length == 0
            ? []
            : new HashSet<string>(value.Split([", "], StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

    /// <summary>
    ///     AB#6268 rows M2–M5, M13, M14: method parameters (type and interface methods).
    /// </summary>
    private static (CkSemVerLevel, string) ClassifyMethodParameterChange(CkModelChange change, CkCompiledModelRoot current)
    {
        switch (change.ChangeKind)
        {
            case CkModelChangeKind.Added:
                return FindParameter(change.ElementId, current)?.IsOptional switch
                {
                    true => (CkSemVerLevel.Minor, "optional method parameter added — existing calls stay valid (row M2)"),
                    false => (CkSemVerLevel.Major, "required method parameter added — existing calls break (row M3)"),
                    null => (CkSemVerLevel.Major,
                        "added method parameter could not be resolved — defensively classified as major (row M3)")
                };
            case CkModelChangeKind.Removed:
                return (CkSemVerLevel.Major, "method parameter removed (or renamed) — calls passing it break (row M3)");
            case CkModelChangeKind.Modified when change.Property is "valueType" or "valueCkRecordId" or "valueCkEnumId":
                return (CkSemVerLevel.Major, "method parameter type changed — calls break (row M4)");
            case CkModelChangeKind.Modified when change.Property == "isOptional":
                return change.NewValue == "false"
                    ? (CkSemVerLevel.Major, "method parameter made required — calls without it break (row M5)")
                    : (CkSemVerLevel.Minor, "method parameter made optional — relaxation (row M5)");
            case CkModelChangeKind.Modified when change.Property == "sensitive":
                return (CkSemVerLevel.Minor, "method parameter sensitivity changed — logging/audit behaviour only (row M13)");
            default:
                return (CkSemVerLevel.Major,
                    DefensiveDefaultReasonPrefix + "method parameter change — defensively classified as major");
        }
    }

    private static CkMethodParameterDto? FindParameter(string elementId, CkCompiledModelRoot current)
    {
        var methods = (current.Types ?? []).SelectMany(t => (t.Methods ?? []).Select(m => (Id: $"{t.TypeId.FullName}/{m.MethodId}", Method: m)))
            .Concat((current.Interfaces ?? []).SelectMany(i => (i.Methods ?? []).Select(m => (Id: $"{i.InterfaceId.FullName}/{m.MethodId}", Method: m))));
        foreach (var (id, method) in methods)
        {
            var prefix = id + "/";
            if (elementId.StartsWith(prefix, StringComparison.Ordinal))
            {
                return method.Parameters?.FirstOrDefault(p => p.Name == elementId.Substring(prefix.Length));
            }
        }

        return null;
    }

    /// <summary>
    ///     AB#6271 rows D1–D5: declared ranges and floors of a range-retaining model.
    /// </summary>
    private static (CkSemVerLevel, string) ClassifyDependencyRangeChange(CkModelChange change)
    {
        switch (change.ChangeKind)
        {
            case CkModelChangeKind.Added:
                return (CkSemVerLevel.Minor, "purely additive dependency (row D5)");
            case CkModelChangeKind.Removed:
                return (CkSemVerLevel.Major, "consumers may rely on the transitively provided model (defensive, row D5)");
            case CkModelChangeKind.Modified when change.Property == "range":
                var oldMajor = RangeMajor(change.OldValue);
                var newMajor = RangeMajor(change.NewValue);
                return oldMajor == null || newMajor == null || oldMajor != newMajor
                    ? (CkSemVerLevel.Major, "dependency range moved to another major version — transitively breaking (row D4)")
                    : (CkSemVerLevel.Minor, "dependency range changed within the same major (row D2/D3)");
            case CkModelChangeKind.Modified when change.Property == "floor":
                var oldFloor = ParseVersion(change.OldValue);
                var newFloor = ParseVersion(change.NewValue);
                if (oldFloor == null || newFloor == null || oldFloor.Value.Major != newFloor.Value.Major)
                {
                    return (CkSemVerLevel.Major, "dependency floor moved to another major version — transitively breaking (row D4)");
                }

                return newFloor.Value.CompareTo(oldFloor.Value) > 0
                    ? (CkSemVerLevel.Minor, "dependency floor raised — tenants need the newer dependency version (row D1)")
                    : (CkSemVerLevel.Minor, "dependency floor lowered — relaxation (row D2)");
            default:
                return (CkSemVerLevel.Major,
                    DefensiveDefaultReasonPrefix + "dependency range change — defensively classified as major");
        }
    }

    private static int? RangeMajor(string? range)
    {
        if (range == null || range.Length == 0)
        {
            return null;
        }

        try
        {
            return new CkVersionRange(range).MinVersion?.Major;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static CkVersion? ParseVersion(string? version)
    {
        if (version == null || version.Length == 0)
        {
            return null;
        }

        try
        {
            return new CkVersion(version);
        }
        catch (ArgumentException)
        {
            return null;
        }
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
                return (CkSemVerLevel.Major, DefensiveDefaultReasonPrefix + "dependency change — defensively classified as major");
        }
    }

    private static (CkSemVerLevel, string) ClassifyAttributeAssignmentChange(CkModelChange change,
        CkCompiledModelRoot baseline, CkCompiledModelRoot current)
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

            // AB#5187: the per-assignment ownership override. Setting, clearing or changing it
            // moves this one assignment between "seed wins" and "tenant wins" and in/out of
            // ExportRt — same class of change as the definition-level property, so the same level.
            case CkModelChangeKind.Modified when change.Property == "ownership":
                return (CkSemVerLevel.Minor, "blueprint re-apply and export behavior change for this assignment");

            // CK v2 (AB#6269 row T7): access is ordered ReadWrite < ReadOnly < MethodOnly < Hidden. Tightening breaks
            // generic GraphQL clients and dependents (Major), relaxing is Minor. Platform-owner exception: tightening
            // an attribute that is security-sensitive in both versions is Minor + requiresAcknowledge (see Mark).
            case CkModelChangeKind.Modified when change.Property == "access":
                if (!IsAccessTightening(change))
                {
                    return (CkSemVerLevel.Minor,
                        "access/security: access relaxed — GraphQL exposure of this attribute widens (row T7)");
                }

                return IsSecuritySensitiveInBoth(change, baseline, current)
                    ? (CkSemVerLevel.Minor,
                        "access/security: access tightened on a security-sensitive attribute — accepted security " +
                        "exception, requires acknowledge (row T7)")
                    : (CkSemVerLevel.Major,
                        "access/security: access tightened, generic GraphQL clients and dependents lose write/read access (row T7)");

            default:
                return (CkSemVerLevel.Major, DefensiveDefaultReasonPrefix + "attribute assignment change — defensively classified as major");
        }
    }

    private static int AccessRank(string? access) => access switch
    {
        nameof(CkAttributeAccessDto.ReadOnly) => 1,
        nameof(CkAttributeAccessDto.MethodOnly) => 2,
        nameof(CkAttributeAccessDto.Hidden) => 3,
        _ => 0
    };

    private static bool IsAccessTightening(CkModelChange change) =>
        change.Property == "access" && AccessRank(change.NewValue) > AccessRank(change.OldValue);

    /// <summary>
    ///     AB#6269: the attribute definition behind an assignment is <c>securitySensitive</c> in the baseline and in
    ///     the current version. Definitions of another model cannot be inspected and never qualify.
    /// </summary>
    private static bool IsSecuritySensitiveInBoth(CkModelChange change, CkCompiledModelRoot baseline,
        CkCompiledModelRoot current)
    {
        bool IsSensitive(CkCompiledModelRoot model)
        {
            var assignment = FindAttributeAssignment(model, change.ElementKind, change.ElementId);
            if (assignment == null || assignment.CkAttributeId.ModelId.Name != model.ModelId.Name)
            {
                return false;
            }

            return model.Attributes?.FirstOrDefault(a =>
                a.AttributeId.FullName == assignment.CkAttributeId.ElementId.FullName)?.SecuritySensitive == true;
        }

        return IsSensitive(baseline) && IsSensitive(current);
    }

    /// <summary>
    ///     AB#6270 rows B1–B4 and the AB#6269 security exception: markers on top of the level. Behavioural changes keep
    ///     their level and are reported in their own section; a unique index on a stable base and a security-sensitive
    ///     access tightening require an acknowledge.
    /// </summary>
    private static CkClassifiedModelChange Mark(CkClassifiedModelChange classified, CkCompiledModelRoot baseline,
        CkCompiledModelRoot current)
    {
        var change = classified.Change;
        var isBehavioural = change switch
        {
            { ElementKind: CkModelElementKind.Attribute, Property: "defaultValues" } => true,
            { ElementKind: CkModelElementKind.Type, Property: "displayNameRule" or "displayDescriptionRule"
                or "enableChangeStreamPreAndPostImages" } => true,
            { ElementKind: CkModelElementKind.TypeAttribute or CkModelElementKind.RecordAttribute
                or CkModelElementKind.AssociationRoleAttribute, Property: "autoCompleteValues" or "autoIncrementReference" } => true,
            { ElementKind: CkModelElementKind.TypeMethod or CkModelElementKind.InterfaceMethod, Property: "timeoutSeconds" } => true,
            { ElementKind: CkModelElementKind.TypeIndex, ChangeKind: CkModelChangeKind.Removed } => true,
            { ElementKind: CkModelElementKind.TypeIndex } => !IsUniqueIndex(change.NewValue),
            _ => false
        };

        if (change is { ElementKind: CkModelElementKind.TypeIndex, ChangeKind: CkModelChangeKind.Added } &&
            IsUniqueIndex(change.NewValue) && IsStableBase(current, OwnerTypeOfIndex(change.ElementId)))
        {
            return classified with
            {
                RequiresAcknowledge = true,
                Reason = "unique index added on a stable base — every derived type in every dependent model gets it, " +
                         "and existing data there may violate it; requires acknowledge (row B4)"
            };
        }

        var requiresAcknowledge = change.ElementKind is CkModelElementKind.TypeAttribute
                                      or CkModelElementKind.RecordAttribute or CkModelElementKind.AssociationRoleAttribute &&
                                  IsAccessTightening(change) && IsSecuritySensitiveInBoth(change, baseline, current);
        return isBehavioural || requiresAcknowledge
            ? classified with { IsBehavioural = isBehavioural, RequiresAcknowledge = requiresAcknowledge }
            : classified;
    }

    private static string OwnerTypeOfIndex(string elementId)
    {
        const string suffix = "/index";
        return elementId.EndsWith(suffix, StringComparison.Ordinal)
            ? elementId.Substring(0, elementId.Length - suffix.Length)
            : elementId;
    }

    /// <summary>
    ///     AB#6270: a stable base is a type other models may derive from. In a <c>ckLanguage: 2</c> model: public, not
    ///     final and effectively <c>derivable: Any</c>. In a v1 model only <c>System/Entity</c> and
    ///     <c>System/Configuration</c> (no new meta-model field, platform-owner decision).
    /// </summary>
    internal static bool IsStableBase(CkCompiledModelRoot model, string typeFullName)
    {
        var type = model.Types?.FirstOrDefault(t => t.TypeId.FullName == typeFullName);
        if (type == null)
        {
            return false;
        }

        if (model.EffectiveCkLanguage >= 2)
        {
            return CkModifiers.ResolveVisibility(type.Visibility) == CkVisibilityDto.Public && !type.IsFinal &&
                   CkModifiers.ResolveDerivable(type.Derivable, model.EffectiveCkLanguage) == CkDerivableDto.Any;
        }

        return model.ModelId.Name == "System" && type.TypeId.Name is "Entity" or "Configuration";
    }

    /// <summary>
    ///     A new association assignment is only additive when the referenced role is not
    ///     mandatory: the rule engine rejects entity creation when an association with
    ///     multiplicity One is missing, so wiring a type to a One-multiplicity role is the
    ///     association-level equivalent of a new required attribute without defaults.
    ///     Roles defined in another model cannot be inspected here and are classified
    ///     defensively.
    /// </summary>
    private static (CkSemVerLevel, string) ClassifyTypeAssociationAdded(CkModelChange change,
        CkCompiledModelRoot current)
    {
        var role = FindAssociationRole(current, change.ElementId);
        if (role == null)
        {
            return (CkSemVerLevel.Major,
                "association role of another model — multiplicity not inspectable, defensively classified as major");
        }

        if (role.OutboundMultiplicity == MultiplicitiesDto.One || role.InboundMultiplicity == MultiplicitiesDto.One)
        {
            return (CkSemVerLevel.Major,
                "new mandatory association (multiplicity One) — existing instances become invalid");
        }

        return (CkSemVerLevel.Minor, "purely additive association");
    }

    private static CkAssociationRoleDto? FindAssociationRole(CkCompiledModelRoot current, string elementId)
    {
        // Element ids of type associations have the shape "<typeFullName>/<roleRef> -> <targetRef>",
        // where roleRef is "<modelName>/<roleFullName>" for self references and
        // "<modelName-major>/<roleFullName>" for foreign references (see CkModelDiffService).
        var arrowIndex = elementId.IndexOf(" -> ", StringComparison.Ordinal);
        if (arrowIndex < 0)
        {
            return null;
        }

        var ownerAndRole = elementId.Substring(0, arrowIndex);
        var ownerSeparator = ownerAndRole.IndexOf('/');
        if (ownerSeparator < 0)
        {
            return null;
        }

        var roleReference = ownerAndRole.Substring(ownerSeparator + 1);
        var roleSeparator = roleReference.IndexOf('/');
        if (roleSeparator < 0)
        {
            return null;
        }

        var modelToken = roleReference.Substring(0, roleSeparator);
        var roleFullName = roleReference.Substring(roleSeparator + 1);
        if (modelToken != current.ModelId.Name)
        {
            // Foreign model — the role definition is not part of the diffed model pair
            return null;
        }

        return current.AssociationRoles?.FirstOrDefault(r => r.AssociationRoleId.FullName == roleFullName);
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
