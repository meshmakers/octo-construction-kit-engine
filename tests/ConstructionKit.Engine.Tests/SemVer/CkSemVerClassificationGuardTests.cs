using System.Reflection;
using System.Text.RegularExpressions;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using Meshmakers.Octo.ConstructionKit.Engine.Tests.CkV2;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer;

/// <summary>
///     Guard against silently unclassified schema evolution (AB#6272, the isRuntimeState lesson applied to
///     compatibility). A meta-model field cannot reach main unless it has a diff <b>and</b> a classifier rule:
///     <list type="number">
///         <item>every public property of the element DTOs is compared (<see cref="CkModelDiffService.ComparedProperties" />)
///             or consciously excluded with a written reason (<see cref="CkModelDiffService.ExcludedProperties" />);</item>
///         <item>every compared property has a probe below that changes it, and the diff emits at least one change
///             for it, of a shape listed in <see cref="CkModelDiffService.EmittableChanges" />;</item>
///         <item>no emitted change and no listed shape reaches the classifier's defensive default
///             ("no classification rule …");</item>
///         <item>every <see cref="CkModelElementKind" /> value has at least one rule;</item>
///         <item>the rule rows of <c>docs/ck-semver-rules.md</c> (row ids N*, I*, M*, T*, E*, R*, A*, B*, D*) and the
///             tests named after them (<c>N1_…</c>) match in both directions.</item>
///     </list>
///     How to satisfy the guard: engine <c>CLAUDE.md</c>, "Touch-point checklist", row 7, and
///     <c>docs/ck-semver-rules.md</c>, "Classification guard". Open items live in <see cref="KnownGaps" /> only;
///     that list shrinks with every F2.1 rule story and must be empty at the F2.1 gate (AB#6273).
/// </summary>
public class CkSemVerClassificationGuardTests
{
    private static readonly Type[] KnownDtoTypes =
    [
        typeof(CkCompiledModelRoot),
        typeof(CkModelRootBase),
        typeof(CkModelPropertiesDto),
        typeof(CkCompiledTypeDto),
        typeof(CkTypeDto),
        typeof(CkTypeWithAttributesDto),
        typeof(CkAttributeDto),
        typeof(CkEnumDto),
        typeof(CkEnumValueDto),
        typeof(CkRecordDto),
        typeof(CkAssociationRoleDto),
        typeof(CkTypeAttributeDto),
        typeof(CkTypeAssociationDto),
        typeof(CkTypeIndexDto),
        typeof(CkIndexFieldsDto),
        typeof(CkAttributeMetaDataDto),
        // CK v2 (AB#5667 / AB#5669)
        typeof(CkInterfaceDto),
        typeof(CkInterfaceAttributeDto),
        typeof(CkInterfaceAssociationDto),
        typeof(CkMethodDto),
        typeof(CkMethodParameterDto),
        typeof(CkMethodResultDto),
        typeof(CkMethodErrorDto),
        typeof(CkMethodAuthorizationDto),
        typeof(CkMethodExecutionDto),
        // CK v2 range retention (AB#6271)
        typeof(CkModelDependencyDto)
    ];

    /// <summary>
    ///     DTO types that are not part of a compiled model's public surface and therefore not diffed, each with
    ///     the reason.
    /// </summary>
    private static readonly IReadOnlyDictionary<Type, string> NotDiffedDtoTypes = new Dictionary<Type, string>
    {
        [typeof(CkCacheRoot)] = "CK cache root, not a compiled model",
        [typeof(CkElementsRootDto)] = "source element file root, compiled into CkCompiledModelRoot",
        [typeof(CkMetaRootDto)] = "source ckModel.yaml root, compiled into CkCompiledModelRoot",
        [typeof(CkModelCompileCandidate)] = "compiler input, not part of the compiled model",
        [typeof(CkModelConfigDto)] = "model configuration file of a consuming project, not part of a model",
        [typeof(CkTypeAssociationTuple)] = "internal tuple, not serialized",
        [typeof(CkCompatibilityDto)] =
            "AB#6295: the author's acknowledgements for this release; a decision about the gate, not model structure",
        [typeof(CkAcknowledgeDto)] = "AB#6295: one acknowledgement entry of CkCompatibilityDto"
    };

    /// <summary>
    ///     Rule rows of the CK v2 compatibility table (concept §4.3.2, refined in the F2.1 rule stories). Each row
    ///     needs a row in <c>docs/ck-semver-rules.md</c> and a test whose name starts with the row id, unless it is
    ///     a <see cref="KnownGaps" /> entry.
    /// </summary>
    internal static readonly IReadOnlyList<string> ExpectedRuleRows =
    [
        .. Rows("N", 5), .. Rows("I", 12), .. Rows("M", 14), .. Rows("T", 7), .. Rows("E", 2), .. Rows("R", 2),
        .. Rows("A", 3), .. Rows("B", 4), .. Rows("D", 7)
    ];

    /// <summary>
    ///     The one place for known gaps of the classification guard (AB#6272): item → the story that closes it.
    ///     Items: <c>row:&lt;id&gt;</c> (rule row without docs row or test), <c>property:&lt;Dto&gt;.&lt;Property&gt;</c> (a
    ///     temporary exclusion that must become a diff with a rule), <c>dto:&lt;Dto&gt;</c> (a DTO type not diffed yet).
    ///     The list only shrinks: an entry that is no longer a gap fails <see cref="KnownGaps_AreStillGaps" />.
    ///     It must be empty when F2.1 closes (checked in AB#6273).
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> KnownGaps = BuildKnownGaps();

    /// <remarks>
    ///     Empty since the F2.1 rule stories (AB#6266–AB#6271) landed. A later rule story may add entries of the form
    ///     <c>["row:X1"] = "AB#…"</c> while it is in progress; the list must be empty again at its feature gate.
    /// </remarks>
    private static Dictionary<string, string> BuildKnownGaps() => new(StringComparer.Ordinal);

    private static IEnumerable<string> Rows(string prefix, int count) =>
        Enumerable.Range(1, count).Select(i => $"{prefix}{i}");

    private static readonly Regex RowIdPattern = new(@"^(?<id>[NIMTERABD]\d{1,2})$", RegexOptions.Compiled);
    private static readonly Regex RowTestNamePattern = new(@"^(?<id>[NIMTERABD]\d{1,2})_", RegexOptions.Compiled);
    private static readonly Regex DocsRowPattern =
        new(@"^\|\s*(?<id>[NIMTERABD]\d{1,2})\s*\|", RegexOptions.Compiled | RegexOptions.Multiline);

    // ── 1. DTO types and properties ──────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     F1.1-S1 (AB#5904): a NEW DTO type (not just a new property) must not slip past the diff either — every
    ///     public class/record in the DataTransferObjects namespace is either checked above or consciously excluded.
    /// </summary>
    [Fact]
    public void EveryDtoType_IsKnownOrConsciouslyExcluded()
    {
        var unclassified = typeof(CkCompiledModelRoot).Assembly.GetTypes()
            .Where(t => t.IsPublic && t.IsClass && t.Namespace == typeof(CkCompiledModelRoot).Namespace &&
                        !t.IsSubclassOf(typeof(Attribute)) && !(t.IsAbstract && t.IsSealed))
            .Where(t => !KnownDtoTypes.Contains(t) && !NotDiffedDtoTypes.ContainsKey(t))
            .Select(t => t.Name)
            .ToList();

        Assert.True(unclassified.Count == 0,
            "DTO types neither registered in KnownDtoTypes/CkModelDiffService.ComparedProperties nor excluded: " +
            string.Join(", ", unclassified));
        Assert.All(NotDiffedDtoTypes, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value),
            $"Excluded DTO type '{pair.Key.Name}' has no written reason."));
    }

    public static TheoryData<Type> ElementDtoTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var dtoType in KnownDtoTypes)
        {
            data.Add(dtoType);
        }

        return data;
    }

    /// <summary>
    ///     AB#6272 AC 1: a public DTO property fails the guard (named) unless it is compared (and then proven by a
    ///     probe, see <see cref="EveryComparedProperty_HasAProbe" />) or excluded with a written reason.
    /// </summary>
    [Theory]
    [MemberData(nameof(ElementDtoTypes))]
    public void EveryDtoProperty_IsComparedOrExcludedWithReason(Type dtoType)
    {
        var compared = CkModelDiffService.ComparedProperties.TryGetValue(dtoType, out var c) ? c : [];
        var excluded = CkModelDiffService.ExcludedProperties.TryGetValue(dtoType, out var e)
            ? e
            : new Dictionary<string, string>();
        Assert.True(compared.Count + excluded.Count > 0,
            $"DTO type '{dtoType.Name}' is registered neither in CkModelDiffService.ComparedProperties nor in " +
            "CkModelDiffService.ExcludedProperties.");

        var declaredProperties = dtoType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(p => p.Name)
            .ToHashSet();

        var unaccounted = declaredProperties.Except(compared).Except(excluded.Keys).ToList();
        Assert.True(unaccounted.Count == 0,
            $"Properties of '{dtoType.Name}' without a diff/classification decision: " +
            $"{string.Join(", ", unaccounted)}. Compare them in CkModelDiffService (ComparedProperties + diff + a " +
            "probe in CkSemVerClassificationGuardTests + a classifier rule) or exclude them with a written reason " +
            "(ExcludedProperties), and extend docs/ck-semver-rules.md.");

        var both = compared.Intersect(excluded.Keys).ToList();
        Assert.True(both.Count == 0,
            $"Properties of '{dtoType.Name}' are both compared and excluded: {string.Join(", ", both)}.");

        var withoutReason = excluded.Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Select(pair => pair.Key).ToList();
        Assert.True(withoutReason.Count == 0,
            $"Excluded properties of '{dtoType.Name}' without a written reason: {string.Join(", ", withoutReason)}.");

        var stale = compared.Concat(excluded.Keys).Except(declaredProperties).ToList();
        Assert.True(stale.Count == 0,
            $"CkModelDiffService registers unknown properties of '{dtoType.Name}' (renamed or removed?): " +
            $"{string.Join(", ", stale)}.");
    }

    [Fact]
    public void PropertyRegistry_CoversExactlyTheKnownDtoTypes()
    {
        var expected = KnownDtoTypes.ToHashSet();
        var actual = CkModelDiffService.AccountedProperties.Keys.ToHashSet();

        Assert.True(expected.SetEquals(actual),
            "The DTO type closure of the diff changed. Update the guard test's type list and " +
            "make a conscious diff/classification decision for new types. " +
            $"Missing in registry: {string.Join(", ", expected.Except(actual).Select(t => t.Name))}; " +
            $"unexpected in registry: {string.Join(", ", actual.Except(expected).Select(t => t.Name))}.");
    }

    // ── 2. Probes: every compared property is really diffed and classified ──────────────────────────────

    public static TheoryData<string> ComparedPropertyKeys()
    {
        var data = new TheoryData<string>();
        foreach (var key in CkModelDiffService.ComparedProperties
                     .SelectMany(pair => pair.Value.Select(property => $"{pair.Key.Name}.{property}"))
                     .Order(StringComparer.Ordinal))
        {
            data.Add(key);
        }

        return data;
    }

    /// <summary>
    ///     AB#6272 AC 1 + 2: a compared property needs a probe; the probe's change must produce at least one diff
    ///     change, every change must have a listed shape and an explicit classifier rule.
    /// </summary>
    [Theory]
    [MemberData(nameof(ComparedPropertyKeys))]
    public void EveryComparedProperty_HasAProbe_ThatIsDiffedAndClassified(string propertyKey)
    {
        Assert.True(PropertyProbes.TryGetValue(propertyKey, out var probe),
            $"Compared property '{propertyKey}' has no probe in CkSemVerClassificationGuardTests.PropertyProbes. " +
            "Add one that changes the property, so the guard can prove the diff and the classifier rule.");

        var baseline = CreateProbeModel();
        var current = CreateProbeModel();
        probe(current);

        var changes = new CkModelDiffService().Diff(baseline, current);
        Assert.True(changes.Count > 0,
            $"Probe of '{propertyKey}' changed the model but CkModelDiffService emitted no change: the property " +
            "is registered as compared but not diffed.");

        var unlisted = changes.Select(CkModelChangeShape.Of).Distinct()
            .Where(shape => !CkModelDiffService.EmittableChanges.Contains(shape)).ToList();
        Assert.True(unlisted.Count == 0,
            $"Probe of '{propertyKey}' emitted change shapes missing in CkModelDiffService.EmittableChanges: " +
            string.Join(", ", unlisted));

        var unclassified = new CkSemVerClassifier().Classify(changes, baseline, current)
            .Where(CkSemVerClassifier.IsDefensiveDefault)
            .Select(c => CkModelChangeShape.Of(c.Change)).Distinct().ToList();
        Assert.True(unclassified.Count == 0,
            $"Probe of '{propertyKey}' reaches the classifier's defensive default (no explicit rule) for: " +
            $"{string.Join(", ", unclassified)}. Add a rule to CkSemVerClassifier and a row to docs/ck-semver-rules.md.");
    }

    [Fact]
    public void EveryComparedProperty_HasAProbe()
    {
        var compared = CkModelDiffService.ComparedProperties
            .SelectMany(pair => pair.Value.Select(property => $"{pair.Key.Name}.{property}")).ToHashSet();
        var missing = compared.Except(PropertyProbes.Keys).Order(StringComparer.Ordinal).ToList();
        var stale = PropertyProbes.Keys.Except(compared).Order(StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0, $"Compared properties without a probe: {string.Join(", ", missing)}.");
        Assert.True(stale.Count == 0, $"Probes for properties that are not compared: {string.Join(", ", stale)}.");
    }

    // ── 3. Shapes and element kinds reach explicit rules ─────────────────────────────────────────────────

    public static TheoryData<string> EmittableShapes()
    {
        var data = new TheoryData<string>();
        foreach (var shape in CkModelDiffService.EmittableChanges)
        {
            data.Add(shape.ToString());
        }

        return data;
    }

    /// <summary>
    ///     AB#6272 AC 2: one synthetic change per shape the diff can emit; a shape that reaches the defensive
    ///     default fails with element kind, change kind and property.
    /// </summary>
    [Theory]
    [MemberData(nameof(EmittableShapes))]
    public void EveryEmittableShape_ReachesAnExplicitRule(string shapeName)
    {
        var shape = CkModelDiffService.EmittableChanges.Single(s => s.ToString() == shapeName);
        foreach (var change in SyntheticChanges(shape))
        {
            var classified = Classify(change);
            Assert.False(CkSemVerClassifier.IsDefensiveDefault(classified),
                $"No classification rule for {shape} (old '{change.OldValue}', new '{change.NewValue}'): the change " +
                "reaches the defensive default of CkSemVerClassifier. Add an explicit rule and a docs row.");
        }
    }

    [Fact]
    public void EmittableChanges_HasNoDuplicates()
    {
        var duplicates = CkModelDiffService.EmittableChanges.GroupBy(s => s).Where(g => g.Count() > 1)
            .Select(g => g.Key.ToString()).ToList();
        Assert.True(duplicates.Count == 0, $"Duplicate shapes: {string.Join(", ", duplicates)}.");
    }

    /// <summary>
    ///     AB#6272 AC 3: a <see cref="CkModelElementKind" /> value the diff never emits, or whose shapes all reach
    ///     the defensive default, has no rule.
    /// </summary>
    [Fact]
    public void EveryElementKind_HasAtLeastOneRule()
    {
        var withoutRule = Enum.GetValues<CkModelElementKind>()
            .Where(kind => !CkModelDiffService.EmittableChanges.Where(s => s.ElementKind == kind)
                .SelectMany(SyntheticChanges)
                .Any(change => !CkSemVerClassifier.IsDefensiveDefault(Classify(change))))
            .ToList();

        Assert.True(withoutRule.Count == 0,
            $"CkModelElementKind values without a classification rule: {string.Join(", ", withoutRule)}. " +
            "Emit them from CkModelDiffService (EmittableChanges) and add rules to CkSemVerClassifier.");
    }

    // ── 4. Rule rows: docs ↔ tests ───────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     AB#6272 AC 4: a row id in <c>docs/ck-semver-rules.md</c> without a test of that name, or a row test
    ///     without a docs row, fails; so does an expected row that is neither documented and tested nor a known gap.
    /// </summary>
    [Fact]
    public void RuleRows_InDocsAndTests_Match()
    {
        var documented = ReadDocumentedRowIds();
        var tested = FindRowTestIds();
        var problems = new List<string>();

        problems.AddRange(documented.Except(tested).Order(StringComparer.Ordinal)
            .Select(id => $"row {id} is in docs/ck-semver-rules.md but has no test named '{id}_…'"));
        problems.AddRange(tested.Except(documented).Order(StringComparer.Ordinal)
            .Select(id => $"test '{id}_…' has no row {id} in docs/ck-semver-rules.md"));
        problems.AddRange(documented.Union(tested).Except(ExpectedRuleRows).Order(StringComparer.Ordinal)
            .Select(id => $"row {id} is not an expected rule row (add it to ExpectedRuleRows)"));
        problems.AddRange(ExpectedRuleRows
            .Where(id => !(documented.Contains(id) && tested.Contains(id)) && !KnownGaps.ContainsKey($"row:{id}"))
            .Select(id => $"rule row {id} is neither documented and tested nor a known gap"));

        Assert.True(problems.Count == 0, "Rule row drift: " + string.Join("; ", problems) + ".");
    }

    // ── 5. Known gaps ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     AB#6272 AC 6: every known gap names the story that closes it, and an entry that is no longer a gap must
    ///     be removed (the list only shrinks).
    /// </summary>
    [Fact]
    public void KnownGaps_AreStillGaps()
    {
        var documented = ReadDocumentedRowIds();
        var tested = FindRowTestIds();
        var problems = new List<string>();

        foreach (var (item, story) in KnownGaps)
        {
            if (!Regex.IsMatch(story, @"^AB#\d+$"))
            {
                problems.Add($"{item}: '{story}' is not a story reference (AB#<id>)");
            }

            var separator = item.IndexOf(':');
            var kind = item[..separator];
            var value = item[(separator + 1)..];
            switch (kind)
            {
                case "row" when !ExpectedRuleRows.Contains(value):
                    problems.Add($"{item}: not an expected rule row");
                    break;
                case "row" when documented.Contains(value) && tested.Contains(value):
                    problems.Add($"{item}: documented and tested now, remove it from KnownGaps ({story})");
                    break;
                case "property":
                {
                    var dot = value.IndexOf('.');
                    var type = KnownDtoTypes.SingleOrDefault(t => t.Name == value[..dot]);
                    var property = value[(dot + 1)..];
                    if (type == null || !CkModelDiffService.ExcludedProperties.TryGetValue(type, out var excluded) ||
                        !excluded.ContainsKey(property))
                    {
                        problems.Add($"{item}: no longer an excluded property, remove it from KnownGaps ({story})");
                    }

                    break;
                }
                case "dto" when KnownDtoTypes.Any(t => t.Name == value):
                    problems.Add($"{item}: the DTO type is diffed now, remove it from KnownGaps ({story})");
                    break;
                case "row" or "property" or "dto":
                    break;
                default:
                    problems.Add($"{item}: unknown gap kind '{kind}'");
                    break;
            }
        }

        Assert.True(problems.Count == 0, "Stale or invalid known gaps: " + string.Join("; ", problems) + ".");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────────

    private static CkClassifiedModelChange Classify(CkModelChange change)
    {
        var model = CreateProbeModel();
        return new CkSemVerClassifier().Classify([change], model, model).Single();
    }

    /// <summary>
    ///     Synthetic changes of one shape with value pairs that exercise both branches of value-dependent rules.
    /// </summary>
    private static IEnumerable<CkModelChange> SyntheticChanges(CkModelChangeShape shape)
    {
        var elementId = shape.ElementKind switch
        {
            CkModelElementKind.Dependency => "Base",
            CkModelElementKind.TypeAssociation => $"Machine/{SemVerTestModels.ModelName}/Parent -> {SemVerTestModels.ModelName}/Machine",
            _ => "Machine/SerialNumber"
        };

        if (shape.ChangeKind == CkModelChangeKind.Added)
        {
            return [new CkModelChange { ChangeKind = shape.ChangeKind, ElementKind = shape.ElementKind, ElementId = elementId, NewValue = "x" }];
        }

        if (shape.ChangeKind == CkModelChangeKind.Removed)
        {
            return [new CkModelChange { ChangeKind = shape.ChangeKind, ElementKind = shape.ElementKind, ElementId = elementId, OldValue = "x" }];
        }

        (string? Old, string? New)[] values = shape.Property switch
        {
            "version" => [("1.0.0", "1.1.0"), ("1.0.0", "2.0.0")],
            "ckLanguage" => [("1", "2"), ("2", "1")],
            _ => [("false", "true"), ("true", "false"), (null, "x"), ("x", null)]
        };
        return values.Select(v => new CkModelChange
        {
            ChangeKind = shape.ChangeKind, ElementKind = shape.ElementKind, ElementId = elementId,
            Property = shape.Property, OldValue = v.Old, NewValue = v.New
        });
    }

    private static HashSet<string> ReadDocumentedRowIds()
    {
        var docs = File.ReadAllText(FindRulesDocument());
        return DocsRowPattern.Matches(docs).Select(m => m.Groups["id"].Value).ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> FindRowTestIds()
    {
        return typeof(CkSemVerClassificationGuardTests).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes<FactAttribute>(true).Any())
            .Select(m => RowTestNamePattern.Match(m.Name))
            .Where(m => m.Success && RowIdPattern.IsMatch(m.Groups["id"].Value))
            .Select(m => m.Groups["id"].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string FindRulesDocument()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "ck-semver-rules.md");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("docs/ck-semver-rules.md not found above " + AppContext.BaseDirectory);
    }

    // ── Probe model and probes ───────────────────────────────────────────────────────────────────────────

    private const string M = SemVerTestModels.ModelName;

    /// <summary>
    ///     The CK v2 SemVer model plus every member a probe needs (attribute metadata, a weighted text index,
    ///     interface extends / association / method).
    /// </summary>
    private static CkCompiledModelRoot CreateProbeModel()
    {
        var model = CkV2TestModels.CreateModel();
        SemVerTestModels.GetAttribute(model, "SerialNumber").MetaData =
            [new CkAttributeMetaDataDto { Key = "unit", Value = "mm", Description = "Unit" }];
        Machine(model).Indexes!.Add(new CkTypeIndexDto
        {
            IndexType = IndexTypeDto.Ascending, Language = "en",
            Fields = [new CkIndexFieldsDto { AttributePaths = ["state"], Weight = 2 }]
        });
        model.Interfaces!.Add(new CkInterfaceDto
        {
            InterfaceId = "Named-1",
            Attributes = [new CkInterfaceAttributeDto { CkAttributeId = $"{M}/SerialNumber", AttributeName = "Name" }]
        });
        var serialized = Interface(model);
        serialized.Extends = [new CkId<CkInterfaceId>($"{M}/Named-1")];
        serialized.Associations =
        [
            new CkInterfaceAssociationDto
            {
                CkRoleId = $"{M}/Parent", TargetCkTypeId = $"{M}/Machine", Multiplicity = MultiplicitiesDto.N,
                IsOptional = true
            }
        ];
        serialized.Methods = [CkV2TestModels.CreateFullMethod()];
        model.DependencyRanges = [new CkModelDependencyDto { Range = "Base-[1.2,2.0)", Floor = "1.2.3" }];
        return model;
    }

    private static CkCompiledTypeDto Machine(CkCompiledModelRoot m) => SemVerTestModels.GetMachine(m);
    private static CkTypeAttributeDto Assignment(CkCompiledModelRoot m) => Machine(m).Attributes!.First();
    private static CkTypeAssociationDto Association(CkCompiledModelRoot m) => Machine(m).Associations!.Single();
    private static CkTypeIndexDto Index(CkCompiledModelRoot m) => Machine(m).Indexes!.Last();
    private static CkAttributeDto Attribute(CkCompiledModelRoot m) => SemVerTestModels.GetAttribute(m, "SerialNumber");
    private static CkEnumValueDto EnumValue(CkCompiledModelRoot m) => SemVerTestModels.GetEnum(m).Values.First();
    private static CkInterfaceDto Interface(CkCompiledModelRoot m) => m.Interfaces!.Single(i => i.InterfaceId.Name == "Serialized");
    private static CkMethodDto Method(CkCompiledModelRoot m) => Machine(m).Methods!.Single(x => x.MethodId == "ChangePassword-1");
    private static CkMethodParameterDto Parameter(CkCompiledModelRoot m) => Method(m).Parameters!.First();

    /// <summary>
    ///     One probe per compared DTO property (key <c>&lt;Dto&gt;.&lt;Property&gt;</c>): it changes the property on the
    ///     current model. A new compared property needs a probe here.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Action<CkCompiledModelRoot>> PropertyProbes =
        new Dictionary<string, Action<CkCompiledModelRoot>>
        {
            ["CkCompiledModelRoot.Dependencies"] = m => m.Dependencies = [new CkModelId("Base", "1.3.0")],
            ["CkCompiledModelRoot.DependencyRanges"] = m =>
                m.DependencyRanges!.Add(new CkModelDependencyDto { Range = "Other-[1.0,2.0)", Floor = "1.0.0" }),
            ["CkModelDependencyDto.Range"] = m => m.DependencyRanges![0].Range = "Base-[1.2,3.0)",
            ["CkModelDependencyDto.Floor"] = m => m.DependencyRanges![0].Floor = "1.4.0",
            ["CkModelRootBase.Types"] = m => m.Types!.Add(new CkCompiledTypeDto { TypeId = "Extra", DerivedFromCkTypeId = "Base/Entity" }),
            ["CkModelRootBase.AssociationRoles"] = m => m.AssociationRoles!.Add(new CkAssociationRoleDto
            {
                AssociationRoleId = "Extra", InboundName = "Ins", OutboundName = "Outs",
                InboundMultiplicity = MultiplicitiesDto.N, OutboundMultiplicity = MultiplicitiesDto.N
            }),
            ["CkModelRootBase.Attributes"] = m => m.Attributes!.Add(new CkAttributeDto { AttributeId = "Extra", ValueType = AttributeValueTypesDto.String }),
            ["CkModelRootBase.Records"] = m => m.Records!.Add(new CkRecordDto { RecordId = "Extra" }),
            ["CkModelRootBase.Enums"] = m => m.Enums!.Add(new CkEnumDto { EnumId = "Extra", Values = [] }),
            ["CkModelRootBase.Interfaces"] = m => m.Interfaces!.Add(new CkInterfaceDto { InterfaceId = "Extra-1" }),
            ["CkModelPropertiesDto.Description"] = m => m.Description = "changed",
            ["CkModelPropertiesDto.CkLanguage"] = m => m.CkLanguage = 3,
            ["CkCompiledTypeDto.IsCollectionRoot"] = m => Machine(m).IsCollectionRoot = true,
            ["CkTypeDto.TypeId"] = m => Machine(m).TypeId = "Machine2",
            ["CkTypeDto.DerivedFromCkTypeId"] = m => Machine(m).DerivedFromCkTypeId = "Base/Other",
            ["CkTypeDto.IsFinal"] = m => Machine(m).IsFinal = true,
            ["CkTypeDto.IsAbstract"] = m => Machine(m).IsAbstract = true,
            ["CkTypeDto.Indexes"] = m => Machine(m).Indexes!.Add(new CkTypeIndexDto
            {
                IndexType = IndexTypeDto.Ascending, Fields = [new CkIndexFieldsDto { AttributePaths = ["other"] }]
            }),
            ["CkTypeDto.Associations"] = m => Machine(m).Associations!.Add(new CkTypeAssociationDto
            {
                CkRoleId = $"{M}/Parent", TargetCkTypeId = "Base/Entity"
            }),
            ["CkTypeDto.EnableChangeStreamPreAndPostImages"] = m => Machine(m).EnableChangeStreamPreAndPostImages = true,
            ["CkTypeDto.Description"] = m => Machine(m).Description = "changed",
            ["CkTypeDto.DisplayNameRule"] = m => Machine(m).DisplayNameRule = "{serialNumber}",
            ["CkTypeDto.DisplayDescriptionRule"] = m => Machine(m).DisplayDescriptionRule = "{serialNumber}",
            ["CkTypeDto.OwnerAttributePath"] = m => Machine(m).OwnerAttributePath = "serialNumber",
            ["CkTypeDto.Implements"] = m => Machine(m).Implements!.Add(new CkId<CkInterfaceId>($"{M}/Named-1")),
            ["CkTypeDto.Methods"] = m => Machine(m).Methods!.Add(new CkMethodDto { MethodId = "Extra-1" }),
            ["CkTypeDto.Visibility"] = m => Machine(m).Visibility = CkVisibilityDto.Internal,
            ["CkTypeDto.Derivable"] = m => Machine(m).Derivable = CkDerivableDto.Model,
            ["CkTypeWithAttributesDto.Attributes"] = m => Machine(m).Attributes!.Add(new CkTypeAttributeDto
            {
                CkAttributeId = $"{M}/SerialNumber", AttributeName = "Extra", IsOptional = true
            }),
            ["CkAttributeDto.AttributeId"] = m => Attribute(m).AttributeId = "SerialNumber2",
            ["CkAttributeDto.ValueType"] = m => Attribute(m).ValueType = AttributeValueTypesDto.Int,
            ["CkAttributeDto.ValueCkRecordId"] = m => Attribute(m).ValueCkRecordId = $"{M}/Address",
            ["CkAttributeDto.ValueCkEnumId"] = m => Attribute(m).ValueCkEnumId = $"{M}/State",
            ["CkAttributeDto.DefaultValues"] = m => Attribute(m).DefaultValues = ["default"],
            ["CkAttributeDto.IsRuntimeState"] = m => Attribute(m).IsRuntimeState = true,
            ["CkAttributeDto.Ownership"] = m => Attribute(m).Ownership = AttributeOwnershipDto.TenantOwned,
            ["CkAttributeDto.Description"] = m => Attribute(m).Description = "changed",
            ["CkAttributeDto.MetaData"] = m => Attribute(m).MetaData = [],
            ["CkAttributeDto.Visibility"] = m => Attribute(m).Visibility = CkVisibilityDto.Internal,
            ["CkAttributeDto.SecuritySensitive"] = m => Attribute(m).SecuritySensitive = true,
            ["CkEnumDto.EnumId"] = m => SemVerTestModels.GetEnum(m).EnumId = "State2",
            ["CkEnumDto.UseFlags"] = m => SemVerTestModels.GetEnum(m).UseFlags = true,
            ["CkEnumDto.IsExtensible"] = m => SemVerTestModels.GetEnum(m).IsExtensible = true,
            ["CkEnumDto.Values"] = m => SemVerTestModels.GetEnum(m).Values.Add(new CkEnumValueDto { Key = 2, Name = "Standby" }),
            ["CkEnumDto.Description"] = m => SemVerTestModels.GetEnum(m).Description = "changed",
            ["CkEnumDto.Visibility"] = m => SemVerTestModels.GetEnum(m).Visibility = CkVisibilityDto.Internal,
            ["CkEnumValueDto.Key"] = m => EnumValue(m).Key = 7,
            ["CkEnumValueDto.Name"] = m => EnumValue(m).Name = "Off2",
            ["CkEnumValueDto.Description"] = m => EnumValue(m).Description = "changed",
            ["CkEnumValueDto.IsExtension"] = m => EnumValue(m).IsExtension = true,
            ["CkRecordDto.RecordId"] = m => SemVerTestModels.GetRecord(m).RecordId = "Address2",
            ["CkRecordDto.DerivedFromCkRecordId"] = m => SemVerTestModels.GetRecord(m).DerivedFromCkRecordId = "Base/Record",
            ["CkRecordDto.IsFinal"] = m => SemVerTestModels.GetRecord(m).IsFinal = true,
            ["CkRecordDto.IsAbstract"] = m => SemVerTestModels.GetRecord(m).IsAbstract = true,
            ["CkRecordDto.Description"] = m => SemVerTestModels.GetRecord(m).Description = "changed",
            ["CkRecordDto.RecordKey"] = m => SemVerTestModels.GetRecord(m).RecordKey = "Street",
            ["CkRecordDto.Visibility"] = m => SemVerTestModels.GetRecord(m).Visibility = CkVisibilityDto.Internal,
            ["CkRecordDto.Derivable"] = m => SemVerTestModels.GetRecord(m).Derivable = CkDerivableDto.Model,
            ["CkAssociationRoleDto.AssociationRoleId"] = m => SemVerTestModels.GetRole(m).AssociationRoleId = "Parent2",
            ["CkAssociationRoleDto.InboundName"] = m => SemVerTestModels.GetRole(m).InboundName = "Kids",
            ["CkAssociationRoleDto.OutboundName"] = m => SemVerTestModels.GetRole(m).OutboundName = "Owner",
            ["CkAssociationRoleDto.InboundMultiplicity"] = m => SemVerTestModels.GetRole(m).InboundMultiplicity = MultiplicitiesDto.One,
            ["CkAssociationRoleDto.OutboundMultiplicity"] = m => SemVerTestModels.GetRole(m).OutboundMultiplicity = MultiplicitiesDto.N,
            ["CkAssociationRoleDto.Description"] = m => SemVerTestModels.GetRole(m).Description = "changed",
            ["CkAssociationRoleDto.Visibility"] = m => SemVerTestModels.GetRole(m).Visibility = CkVisibilityDto.Internal,
            ["CkTypeAttributeDto.CkAttributeId"] = m => Assignment(m).CkAttributeId = $"{M}/WithDefault",
            ["CkTypeAttributeDto.AttributeName"] = m => Assignment(m).AttributeName = "Serial2",
            ["CkTypeAttributeDto.AutoCompleteValues"] = m => Assignment(m).AutoCompleteValues = ["a"],
            ["CkTypeAttributeDto.AutoIncrementReference"] = m => Assignment(m).AutoIncrementReference = "counter",
            ["CkTypeAttributeDto.IsOptional"] = m => Assignment(m).IsOptional = true,
            ["CkTypeAttributeDto.Ownership"] = m => Assignment(m).Ownership = AttributeOwnershipDto.TenantOwned,
            ["CkTypeAttributeDto.Access"] = m => Assignment(m).Access = CkAttributeAccessDto.ReadOnly,
            ["CkTypeAssociationDto.CkRoleId"] = m => Association(m).CkRoleId = "Base/Role",
            ["CkTypeAssociationDto.TargetCkTypeId"] = m => Association(m).TargetCkTypeId = "Base/Entity",
            ["CkTypeAssociationDto.TargetCkAttributeIds"] = m => Association(m).TargetCkAttributeIds = [$"{M}/SerialNumber"],
            ["CkTypeAssociationDto.TargetCkInterfaceId"] = m => Association(m).TargetCkInterfaceId = $"{M}/Serialized-1",
            ["CkTypeIndexDto.IndexType"] = m => Index(m).IndexType = IndexTypeDto.Unique,
            ["CkTypeIndexDto.Language"] = m => Index(m).Language = "de",
            ["CkTypeIndexDto.Fields"] = m => Index(m).Fields.Add(new CkIndexFieldsDto { AttributePaths = ["serialNumber"] }),
            ["CkIndexFieldsDto.Weight"] = m => Index(m).Fields.Single().Weight = 3,
            ["CkIndexFieldsDto.AttributePaths"] = m => Index(m).Fields.Single().AttributePaths = ["other"],
            ["CkAttributeMetaDataDto.Key"] = m => Attribute(m).MetaData!.Single().Key = "unit2",
            ["CkAttributeMetaDataDto.Value"] = m => Attribute(m).MetaData!.Single().Value = "cm",
            ["CkAttributeMetaDataDto.Description"] = m => Attribute(m).MetaData!.Single().Description = "changed",
            ["CkInterfaceDto.InterfaceId"] = m => Interface(m).InterfaceId = "Serialized-2",
            ["CkInterfaceDto.Description"] = m => Interface(m).Description = "changed",
            ["CkInterfaceDto.Attributes"] = m => Interface(m).Attributes.Add(new CkInterfaceAttributeDto
            {
                CkAttributeId = $"{M}/WithDefault", AttributeName = "Extra", IsOptional = true
            }),
            ["CkInterfaceDto.Visibility"] = m => Interface(m).Visibility = CkVisibilityDto.Internal,
            ["CkInterfaceDto.Extends"] = m => Interface(m).Extends = [],
            ["CkInterfaceDto.Associations"] = m => Interface(m).Associations!.Add(new CkInterfaceAssociationDto
            {
                CkRoleId = $"{M}/Parent", TargetCkInterfaceId = $"{M}/Named-1", IsOptional = true
            }),
            ["CkInterfaceDto.Methods"] = m => Interface(m).Methods!.Add(new CkMethodDto { MethodId = "Extra-1" }),
            ["CkInterfaceDto.Deprecated"] = m => Interface(m).Deprecated = true,
            ["CkInterfaceAssociationDto.CkRoleId"] = m => Interface(m).Associations!.Single().CkRoleId = "Base/Role",
            ["CkInterfaceAssociationDto.TargetCkTypeId"] = m => Interface(m).Associations!.Single().TargetCkTypeId = "Base/Entity",
            ["CkInterfaceAssociationDto.TargetCkInterfaceId"] = m =>
            {
                var association = Interface(m).Associations!.Single();
                association.TargetCkTypeId = null;
                association.TargetCkInterfaceId = $"{M}/Named-1";
            },
            ["CkInterfaceAssociationDto.Multiplicity"] = m => Interface(m).Associations!.Single().Multiplicity = MultiplicitiesDto.One,
            ["CkInterfaceAssociationDto.IsOptional"] = m => Interface(m).Associations!.Single().IsOptional = false,
            ["CkInterfaceAttributeDto.CkAttributeId"] = m => Interface(m).Attributes.First().CkAttributeId = $"{M}/WithDefault",
            ["CkInterfaceAttributeDto.AttributeName"] = m => Interface(m).Attributes.First().AttributeName = "Serial2",
            ["CkInterfaceAttributeDto.IsOptional"] = m => Interface(m).Attributes.First().IsOptional = true,
            ["CkMethodDto.MethodId"] = m => Method(m).MethodId = "ChangePassword-2",
            ["CkMethodDto.Kind"] = m => Method(m).Kind = CkMethodKindDto.Static,
            ["CkMethodDto.Description"] = m => Method(m).Description = "changed",
            ["CkMethodDto.Parameters"] = m => Method(m).Parameters!.Add(new CkMethodParameterDto
            {
                Name = "extra", ValueType = AttributeValueTypesDto.String, IsOptional = true
            }),
            ["CkMethodDto.Result"] = m => Method(m).Result = null,
            ["CkMethodDto.Errors"] = m => Method(m).Errors!.Add(new CkMethodErrorDto { Code = "EXTRA" }),
            ["CkMethodDto.Authorization"] = m => Method(m).Authorization = null,
            ["CkMethodDto.Execution"] = m => Method(m).Execution = null,
            ["CkMethodDto.Visibility"] = m => Method(m).Visibility = CkVisibilityDto.Internal,
            ["CkMethodParameterDto.Name"] = m => Parameter(m).Name = "oldPassword",
            ["CkMethodParameterDto.ValueType"] = m => Parameter(m).ValueType = AttributeValueTypesDto.Int,
            ["CkMethodParameterDto.ValueCkRecordId"] = m => Parameter(m).ValueCkRecordId = $"{M}/Address",
            ["CkMethodParameterDto.ValueCkEnumId"] = m => Parameter(m).ValueCkEnumId = $"{M}/State",
            ["CkMethodParameterDto.IsOptional"] = m => Parameter(m).IsOptional = false,
            ["CkMethodParameterDto.Sensitive"] = m => Parameter(m).Sensitive = false,
            ["CkMethodParameterDto.Description"] = m => Parameter(m).Description = "changed",
            ["CkMethodResultDto.ValueType"] = m => Method(m).Result!.ValueType = AttributeValueTypesDto.String,
            ["CkMethodResultDto.ValueCkRecordId"] = m => Method(m).Result!.ValueCkRecordId = "Base/Record",
            ["CkMethodResultDto.ValueCkEnumId"] = m => Method(m).Result!.ValueCkEnumId = $"{M}/State",
            ["CkMethodErrorDto.Code"] = m => Method(m).Errors!.First().Code = "OTHER_CODE",
            ["CkMethodErrorDto.Description"] = m => Method(m).Errors!.First().Description = "changed",
            ["CkMethodAuthorizationDto.Roles"] = m => Method(m).Authorization!.Roles = ["Other"],
            ["CkMethodAuthorizationDto.AllowSelf"] = m => Method(m).Authorization!.AllowSelf = false,
            ["CkMethodAuthorizationDto.Scopes"] = m => Method(m).Authorization!.Scopes = [],
            ["CkMethodExecutionDto.TimeoutSeconds"] = m => Method(m).Execution!.TimeoutSeconds = 60,
            ["CkMethodExecutionDto.Idempotent"] = m => Method(m).Execution!.Idempotent = false
        };
}
