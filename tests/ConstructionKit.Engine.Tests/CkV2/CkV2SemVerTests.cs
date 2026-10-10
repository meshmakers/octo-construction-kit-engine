using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.CkV2;

/// <summary>
///     CK v2 Phase 0 SemVer rules (contract 2.6, docs/ck-semver-rules.md): interface/implements/method additions are
///     Minor, removals and contract changes Major, an access change is Minor with an access/security note,
///     ckLanguage 1→2 is Minor.
/// </summary>
public class CkV2SemVerTests
{
    private readonly CkModelDiffService _diffService = new();
    private readonly CkSemVerClassifier _classifier = new();

    private IReadOnlyList<CkClassifiedModelChange> Classify(CkCompiledModelRoot baseline, CkCompiledModelRoot current)
        => _classifier.Classify(_diffService.Diff(baseline, current), baseline, current);

    private CkSemVerLevel Level(CkCompiledModelRoot baseline, CkCompiledModelRoot current)
        => _classifier.GetRequiredLevel(Classify(baseline, current));

    [Fact]
    public void IdenticalCkV2Models_HaveNoChanges()
    {
        Assert.Empty(_diffService.Diff(CkV2TestModels.CreateModel(), CkV2TestModels.CreateModel()));
    }

    [Fact]
    public void AdoptingCkV2_LanguageInterfaceImplementsMethodsAndAccess_IsMinor()
    {
        // v1 → v2 adoption of the spike shape: ckLanguage 1→2, a new interface, implements, methods, access.
        var classified = Classify(SemVerTestModels.CreateModel(), CkV2TestModels.CreateModel());

        Assert.Contains(classified, c => c.Change is { ElementKind: CkModelElementKind.Model, Property: "ckLanguage" });
        Assert.Contains(classified, c => c.Change is { ElementKind: CkModelElementKind.Interface, ChangeKind: CkModelChangeKind.Added });
        Assert.Contains(classified, c => c.Change is { ElementKind: CkModelElementKind.TypeInterface, ChangeKind: CkModelChangeKind.Added });
        Assert.Contains(classified, c => c.Change is { ElementKind: CkModelElementKind.TypeMethod, ChangeKind: CkModelChangeKind.Added });
        Assert.Contains(classified, c => c.Change is { Property: "access" } && c.Reason.StartsWith("access/security"));
        // AB#6269 (row T7): the access tightenings of the adoption (ReadWrite -> Hidden / ReadOnly) are Major now;
        // everything else of the adoption stays Minor.
        Assert.All(classified.Where(c => c.Change.Property != "access"), c => Assert.Equal(CkSemVerLevel.Minor, c.Level));
        Assert.All(classified.Where(c => c.Change.Property == "access"), c => Assert.Equal(CkSemVerLevel.Major, c.Level));
    }

    [Fact]
    public void CkLanguage_OmittedAndOne_AreEqual_LoweringIsMajor()
    {
        var omitted = SemVerTestModels.CreateModel();
        var one = SemVerTestModels.CreateModel();
        one.CkLanguage = 1;
        Assert.Empty(_diffService.Diff(omitted, one));

        var two = SemVerTestModels.CreateModel();
        two.CkLanguage = 2;
        CkV2TestModels.KeepDerivableAny(two);
        Assert.Equal(CkSemVerLevel.Minor, Level(one, two));
        Assert.Equal(CkSemVerLevel.Major, Level(two, one));
    }

    // F1.1-S4: without derivable: Any, adopting ckLanguage 2 closes every existing type/record (default Model) —
    // Major, as tightening derivable is breaking (concept §4.3.2).
    [Fact]
    public void AdoptingCkV2_WithoutDerivableAny_IsMajor()
    {
        var two = SemVerTestModels.CreateModel();
        two.CkLanguage = 2;

        var classified = Classify(SemVerTestModels.CreateModel(), two);

        Assert.Contains(classified, c => c.Change is { Property: "derivable", OldValue: "Any", NewValue: "Model" } &&
                                         c.Level == CkSemVerLevel.Major);
    }

    [Theory]
    [InlineData("Public", "Internal", CkSemVerLevel.Major)]
    [InlineData("Internal", "Public", CkSemVerLevel.Minor)]
    public void VisibilityChanged_OnEveryElementKind(string before, string after, CkSemVerLevel expected)
    {
        CkCompiledModelRoot Build(CkVisibilityDto visibility)
        {
            var model = CkV2TestModels.CreateModel();
            model.Types!.ForEach(t => t.Visibility = visibility);
            model.Records!.ForEach(r => r.Visibility = visibility);
            model.Enums!.ForEach(e => e.Visibility = visibility);
            model.Attributes!.ForEach(a => a.Visibility = visibility);
            model.AssociationRoles!.ForEach(r => r.Visibility = visibility);
            model.Interfaces!.ForEach(i => i.Visibility = visibility);
            SemVerTestModels.GetMachine(model).Methods!.ForEach(m => m.Visibility = visibility);
            return model;
        }

        var classified = Classify(Build(Enum.Parse<CkVisibilityDto>(before)), Build(Enum.Parse<CkVisibilityDto>(after)));

        foreach (var kind in new[]
                 {
                     CkModelElementKind.Type, CkModelElementKind.Record, CkModelElementKind.Enum,
                     CkModelElementKind.Attribute, CkModelElementKind.AssociationRole, CkModelElementKind.Interface,
                     CkModelElementKind.TypeMethod
                 })
        {
            Assert.Contains(classified, c => c.Change.ElementKind == kind && c.Change.Property == "visibility");
        }

        Assert.All(classified, c => Assert.Equal(expected, c.Level));
    }

    [Theory]
    [InlineData(CkDerivableDto.Any, CkDerivableDto.Model, CkSemVerLevel.Major)]
    [InlineData(CkDerivableDto.Model, CkDerivableDto.Any, CkSemVerLevel.Minor)]
    public void DerivableChanged_OnTypesAndRecords(CkDerivableDto before, CkDerivableDto after, CkSemVerLevel expected)
    {
        CkCompiledModelRoot Build(CkDerivableDto derivable)
        {
            var model = CkV2TestModels.CreateModel();
            model.Types!.ForEach(t => t.Derivable = derivable);
            model.Records!.ForEach(r => r.Derivable = derivable);
            return model;
        }

        var classified = Classify(Build(before), Build(after));

        Assert.Contains(classified, c => c.Change is { ElementKind: CkModelElementKind.Type, Property: "derivable" });
        Assert.Contains(classified, c => c.Change is { ElementKind: CkModelElementKind.Record, Property: "derivable" });
        Assert.All(classified, c => Assert.Equal(expected, c.Level));
    }

    [Fact]
    public void InterfaceRemoved_IsMajor()
    {
        var current = CkV2TestModels.CreateModel();
        current.Interfaces = null;
        SemVerTestModels.GetMachine(current).Implements = null;

        var classified = Classify(CkV2TestModels.CreateModel(), current);

        Assert.Contains(classified, c => c.Change is { ElementKind: CkModelElementKind.Interface, ChangeKind: CkModelChangeKind.Removed } && c.Level == CkSemVerLevel.Major);
        Assert.Contains(classified, c => c.Change is { ElementKind: CkModelElementKind.TypeInterface, ChangeKind: CkModelChangeKind.Removed } && c.Level == CkSemVerLevel.Major);
    }

    [Theory]
    [InlineData("memberAdded")]
    [InlineData("memberRemoved")]
    [InlineData("memberOptionality")]
    [InlineData("memberReference")]
    public void InterfaceModified_IsMajor(string modification)
    {
        var current = CkV2TestModels.CreateModel();
        var members = current.Interfaces!.Single().Attributes;
        switch (modification)
        {
            case "memberAdded":
                members.Add(new CkInterfaceAttributeDto { CkAttributeId = $"{CkV2TestModels.ModelName}/WithDefault", AttributeName = "WithDefault" });
                break;
            case "memberRemoved":
                members.RemoveAt(1);
                break;
            case "memberOptionality":
                members[1].IsOptional = false;
                break;
            case "memberReference":
                members[0].CkAttributeId = $"{CkV2TestModels.ModelName}/WithDefault";
                break;
        }

        var classified = Classify(CkV2TestModels.CreateModel(), current);

        Assert.NotEmpty(classified);
        Assert.All(classified, c => Assert.Equal(CkModelElementKind.InterfaceAttribute, c.Change.ElementKind));
        Assert.Equal(CkSemVerLevel.Major, _classifier.GetRequiredLevel(classified));
    }

    [Fact]
    public void InterfaceDescriptionChanged_IsPatch()
    {
        var current = CkV2TestModels.CreateModel();
        current.Interfaces!.Single().Description = "Another text";

        Assert.Equal(CkSemVerLevel.Patch, Level(CkV2TestModels.CreateModel(), current));
    }

    [Fact]
    public void ImplementsAdded_IsMinor_Removed_IsMajor()
    {
        var without = CkV2TestModels.CreateModel();
        SemVerTestModels.GetMachine(without).Implements = null;

        Assert.Equal(CkSemVerLevel.Minor, Level(without, CkV2TestModels.CreateModel()));
        Assert.Equal(CkSemVerLevel.Major, Level(CkV2TestModels.CreateModel(), without));
    }

    [Fact]
    public void MethodAdded_IsMinor_Removed_IsMajor()
    {
        var current = CkV2TestModels.CreateModel();
        SemVerTestModels.GetMachine(current).Methods!.Add(new CkMethodDto { MethodId = "Calibrate-1" });

        Assert.Equal(CkSemVerLevel.Minor, Level(CkV2TestModels.CreateModel(), current));
        Assert.Equal(CkSemVerLevel.Major, Level(current, CkV2TestModels.CreateModel()));
    }

    // AB#6268: one change per method field (rows M2–M13) plus the readable signature summary (level None);
    // until then a single "signature" change was Major for every field.
    [Theory]
    [InlineData("parameterAdded", CkModelElementKind.MethodParameter, null, CkSemVerLevel.Minor)]
    [InlineData("parameterType", CkModelElementKind.MethodParameter, "valueType", CkSemVerLevel.Major)]
    [InlineData("parameterSensitive", CkModelElementKind.MethodParameter, "sensitive", CkSemVerLevel.Minor)]
    [InlineData("result", CkModelElementKind.TypeMethod, "result", CkSemVerLevel.Major)]
    [InlineData("errors", CkModelElementKind.MethodError, null, CkSemVerLevel.Major)]
    [InlineData("roles", CkModelElementKind.TypeMethod, "roles", CkSemVerLevel.Major)]
    [InlineData("allowSelf", CkModelElementKind.TypeMethod, "allowSelf", CkSemVerLevel.Major)]
    [InlineData("kind", CkModelElementKind.TypeMethod, "kind", CkSemVerLevel.Major)]
    [InlineData("timeout", CkModelElementKind.TypeMethod, "timeoutSeconds", CkSemVerLevel.Minor)]
    [InlineData("idempotent", CkModelElementKind.TypeMethod, "idempotent", CkSemVerLevel.Major)]
    public void MethodFieldChanged_IsClassifiedPerField(string modification, CkModelElementKind expectedKind,
        string? expectedProperty, CkSemVerLevel expectedLevel)
    {
        var current = CkV2TestModels.CreateModel();
        var method = SemVerTestModels.GetMachine(current).Methods![0];
        switch (modification)
        {
            case "parameterAdded":
                method.Parameters!.Add(new CkMethodParameterDto { Name = "extra", ValueType = AttributeValueTypesDto.Int, IsOptional = true });
                break;
            case "parameterType":
                method.Parameters![0].ValueType = AttributeValueTypesDto.Int;
                break;
            case "parameterSensitive":
                method.Parameters![0].Sensitive = false;
                break;
            case "result":
                method.Result = null;
                break;
            case "errors":
                method.Errors!.Add(new CkMethodErrorDto { Code = "NEW_ERROR" });
                break;
            case "roles":
                method.Authorization!.Roles = ["Other"];
                break;
            case "allowSelf":
                method.Authorization!.AllowSelf = false;
                break;
            case "kind":
                method.Kind = CkMethodKindDto.Static;
                break;
            case "timeout":
                method.Execution!.TimeoutSeconds = 60;
                break;
            case "idempotent":
                method.Execution!.Idempotent = false;
                break;
        }

        var classified = Classify(CkV2TestModels.CreateModel(), current);
        var summary = Assert.Single(classified, c => c.Change.Property == "signature");
        Assert.Equal(CkSemVerLevel.None, summary.Level);
        var change = Assert.Single(classified, c => c.Change.Property != "signature");
        Assert.Equal(expectedKind, change.Change.ElementKind);
        Assert.Equal(expectedProperty, change.Change.Property);
        Assert.Equal(expectedLevel, change.Level);
    }

    // Review L16: documentation of parameters and errors is not part of the signature (AB#6268: row M14).
    [Theory]
    [InlineData("parameter")]
    [InlineData("error")]
    public void MethodParameterOrErrorDescriptionChanged_IsPatch(string what)
    {
        var current = CkV2TestModels.CreateModel();
        var method = SemVerTestModels.GetMachine(current).Methods![0];
        if (what == "parameter")
        {
            method.Parameters![0].Description = "Reworded";
        }
        else
        {
            method.Errors![0].Description = "Reworded";
        }

        var change = Assert.Single(Classify(CkV2TestModels.CreateModel(), current));
        Assert.Equal(what == "parameter" ? CkModelElementKind.MethodParameter : CkModelElementKind.MethodError,
            change.Change.ElementKind);
        Assert.Equal("description", change.Change.Property);
        Assert.Equal(CkSemVerLevel.Patch, change.Level);
    }

    // Review L16: ckLanguage is compared numerically; ordinally "10" < "2".
    [Fact]
    public void CkLanguage_IsComparedNumerically()
    {
        var two = SemVerTestModels.CreateModel();
        two.CkLanguage = 2;
        var ten = SemVerTestModels.CreateModel();
        ten.CkLanguage = 10;

        Assert.Equal(CkSemVerLevel.Minor, Level(two, ten));
        Assert.Equal(CkSemVerLevel.Major, Level(ten, two));
    }

    [Fact]
    public void MethodDescriptionChanged_IsPatch()
    {
        var current = CkV2TestModels.CreateModel();
        SemVerTestModels.GetMachine(current).Methods![0].Description = "Other text";

        Assert.Equal(CkSemVerLevel.Patch, Level(CkV2TestModels.CreateModel(), current));
    }

    // AB#6269 (row T7): a stricter access is Major now, a looser one Minor; the note stays.
    [Theory]
    [InlineData(null, CkAttributeAccessDto.Hidden, CkSemVerLevel.Major)]
    [InlineData(CkAttributeAccessDto.Hidden, CkAttributeAccessDto.ReadWrite, CkSemVerLevel.Minor)]
    [InlineData(CkAttributeAccessDto.ReadOnly, CkAttributeAccessDto.MethodOnly, CkSemVerLevel.Major)]
    public void AccessChanged_IsClassifiedByDirectionWithSecurityNote(CkAttributeAccessDto? before,
        CkAttributeAccessDto after, CkSemVerLevel expected)
    {
        var baseline = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(baseline).Attributes![0].Access = before;
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(current).Attributes![0].Access = after;

        var change = Assert.Single(Classify(baseline, current));
        Assert.Equal("access", change.Change.Property);
        Assert.Equal(expected, change.Level);
        Assert.StartsWith("access/security", change.Reason);
    }

    [Fact]
    public void AccessOmittedAndReadWrite_AreEqual()
    {
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(current).Attributes![0].Access = CkAttributeAccessDto.ReadWrite;

        Assert.Empty(_diffService.Diff(SemVerTestModels.CreateModel(), current));
    }

    [Fact]
    public void ChangelogFormatter_LabelsCkV2Kinds()
    {
        var line = CkModelChangeFormatter.Format(new CkModelChange
        {
            ChangeKind = CkModelChangeKind.Added, ElementKind = CkModelElementKind.TypeMethod, ElementId = "Machine-1/Calibrate-1"
        });

        Assert.Equal("Method 'Machine-1/Calibrate-1' added", line);
    }

    // ── F1.1-S5 (AB#5908): interface completion ─────────────────────────────────────────────

    private static CkInterfaceDto Serialized(CkCompiledModelRoot model) =>
        model.Interfaces!.Single(i => i.InterfaceId.FullName == "Serialized-1");

    private static CkCompiledModelRoot WithCompletedInterface()
    {
        var model = CkV2TestModels.CreateModel();
        model.Interfaces!.Add(new CkInterfaceDto
        {
            InterfaceId = "Base-1",
            Attributes = [new CkInterfaceAttributeDto { CkAttributeId = $"{CkV2TestModels.ModelName}/SerialNumber", AttributeName = "SerialNumber" }]
        });
        var serialized = model.Interfaces!.Single(i => i.InterfaceId.FullName == "Serialized-1");
        serialized.Associations =
        [
            new CkInterfaceAssociationDto
            {
                CkRoleId = $"{CkV2TestModels.ModelName}/Parent", TargetCkTypeId = $"{CkV2TestModels.ModelName}/Machine",
                Multiplicity = MultiplicitiesDto.ZeroOrOne
            }
        ];
        serialized.Methods = [new CkMethodDto { MethodId = "Calibrate-1" }];
        return model;
    }

    [Theory]
    // AB#6267: an optional association added (row I2) and an association made optional (row I7) are Minor now;
    // they moved to InterfaceRowTests.
    [InlineData("extends added")]
    [InlineData("association removed")]
    [InlineData("association multiplicity")]
    [InlineData("method added")]
    [InlineData("method removed")]
    [InlineData("method signature")]
    public void InterfaceCompletion_ContractChanges_AreMajor(string modification)
    {
        var current = WithCompletedInterface();
        var serialized = current.Interfaces!.Single(i => i.InterfaceId.FullName == "Serialized-1");
        switch (modification)
        {
            case "extends added":
                serialized.Extends = [$"{CkV2TestModels.ModelName}/Base-1"];
                break;
            case "association removed":
                serialized.Associations = null;
                break;
            case "association multiplicity":
                serialized.Associations![0].Multiplicity = MultiplicitiesDto.N;
                break;
            case "method added":
                serialized.Methods!.Add(new CkMethodDto { MethodId = "Reset-1" });
                break;
            case "method removed":
                serialized.Methods = null;
                break;
            default:
                serialized.Methods![0].Kind = CkMethodKindDto.Static;
                break;
        }

        var classified = Classify(WithCompletedInterface(), current);

        Assert.NotEmpty(classified);
        // AB#6268: the method signature summary is reported with level None next to the field changes.
        Assert.All(classified.Where(c => c.Change.Property != "signature"), c => Assert.Equal(CkSemVerLevel.Major, c.Level));
        Assert.All(classified, c => Assert.Contains(c.Change.ElementKind,
            new[] { CkModelElementKind.InterfaceExtends, CkModelElementKind.InterfaceAssociation, CkModelElementKind.InterfaceMethod }));
    }

    [Fact]
    public void InterfaceExtendsRemoved_IsMajor()
    {
        var baseline = WithCompletedInterface();
        baseline.Interfaces!.Single(i => i.InterfaceId.FullName == "Serialized-1").Extends = [$"{CkV2TestModels.ModelName}/Base-1"];

        var change = Assert.Single(Classify(baseline, WithCompletedInterface()));

        Assert.Equal(CkModelElementKind.InterfaceExtends, change.Change.ElementKind);
        Assert.Equal(CkModelChangeKind.Removed, change.Change.ChangeKind);
        Assert.Equal(CkSemVerLevel.Major, change.Level);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(true, false)]
    [InlineData(true, null)]
    public void InterfaceDeprecated_IsMinor(bool? before, bool? after)
    {
        var baseline = WithCompletedInterface();
        Serialized(baseline).Deprecated = before;
        var current = WithCompletedInterface();
        Serialized(current).Deprecated = after;

        var classified = Classify(baseline, current);

        if ((before ?? false) == (after ?? false))
        {
            Assert.Empty(classified);
            return;
        }

        var change = Assert.Single(classified);
        Assert.Equal("deprecated", change.Change.Property);
        Assert.Equal(CkSemVerLevel.Minor, change.Level);
    }

    [Fact]
    public void InterfaceMethodDescriptionChanged_IsPatch_VisibilityInternal_IsMajor()
    {
        var current = WithCompletedInterface();
        Serialized(current).Methods![0].Description = "Calibrates";
        Assert.Equal(CkSemVerLevel.Patch, Assert.Single(Classify(WithCompletedInterface(), current)).Level);

        Serialized(current).Methods![0].Description = null;
        Serialized(current).Methods![0].Visibility = CkVisibilityDto.Internal;
        var change = Assert.Single(Classify(WithCompletedInterface(), current));
        Assert.Equal(CkModelElementKind.InterfaceMethod, change.Change.ElementKind);
        Assert.Equal(CkSemVerLevel.Major, change.Level);
    }

    [Theory]
    [InlineData(null, "Serialized-1", CkSemVerLevel.Major)]
    [InlineData("Serialized-1", null, CkSemVerLevel.Minor)]
    public void TypeAssociationTargetInterface_SetIsMajor_ClearedIsMinor(string? before, string? after, CkSemVerLevel expected)
    {
        CkCompiledModelRoot Build(string? target)
        {
            var model = CkV2TestModels.CreateModel();
            SemVerTestModels.GetMachine(model).Associations![0].TargetCkInterfaceId =
                target == null ? null : new CkId<CkInterfaceId>($"{CkV2TestModels.ModelName}/{target}");
            return model;
        }

        var change = Assert.Single(Classify(Build(before), Build(after)));

        Assert.Equal("targetCkInterfaceId", change.Change.Property);
        Assert.Equal(expected, change.Level);
    }

    [Fact]
    public void ChangelogFormatter_LabelsInterfaceCompletionKinds()
    {
        Assert.Equal("Interface method 'Serialized-1/Calibrate-1' added", CkModelChangeFormatter.Format(new CkModelChange
        {
            ChangeKind = CkModelChangeKind.Added, ElementKind = CkModelElementKind.InterfaceMethod,
            ElementId = "Serialized-1/Calibrate-1"
        }));
    }
}
