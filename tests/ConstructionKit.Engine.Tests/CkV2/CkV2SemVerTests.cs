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
        Assert.All(classified, c => Assert.Equal(CkSemVerLevel.Minor, c.Level));
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

    [Theory]
    [InlineData("parameterAdded")]
    [InlineData("parameterType")]
    [InlineData("parameterSensitive")]
    [InlineData("result")]
    [InlineData("errors")]
    [InlineData("roles")]
    [InlineData("allowSelf")]
    [InlineData("kind")]
    [InlineData("timeout")]
    [InlineData("idempotent")]
    public void MethodSignatureChanged_IsMajor(string modification)
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

        var change = Assert.Single(Classify(CkV2TestModels.CreateModel(), current));
        Assert.Equal(CkModelElementKind.TypeMethod, change.Change.ElementKind);
        Assert.Equal("signature", change.Change.Property);
        Assert.Equal(CkSemVerLevel.Major, change.Level);
    }

    // Review L16: documentation of parameters and errors is not part of the signature.
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
        Assert.Equal("documentation", change.Change.Property);
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

    [Theory]
    [InlineData(null, CkAttributeAccessDto.Hidden)]
    [InlineData(CkAttributeAccessDto.Hidden, CkAttributeAccessDto.ReadWrite)]
    [InlineData(CkAttributeAccessDto.ReadOnly, CkAttributeAccessDto.MethodOnly)]
    public void AccessChanged_IsMinorWithSecurityNote(CkAttributeAccessDto? before, CkAttributeAccessDto after)
    {
        var baseline = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(baseline).Attributes![0].Access = before;
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(current).Attributes![0].Access = after;

        var change = Assert.Single(Classify(baseline, current));
        Assert.Equal("access", change.Change.Property);
        Assert.Equal(CkSemVerLevel.Minor, change.Level);
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
}
