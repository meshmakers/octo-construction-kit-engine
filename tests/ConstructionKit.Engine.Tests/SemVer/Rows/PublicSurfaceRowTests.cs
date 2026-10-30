using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;
using static Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer.Rows.RowTestSupport;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer.Rows;

/// <summary>
///     AB#6269 rows T1–T7, E1–E2, R1–R2, A1–A3: public types (incl. stable bases), enums, records, attribute
///     definitions, attribute access and the security-sensitivity marker.
/// </summary>
public class PublicSurfaceRowTests
{
    private static CkTypeAttributeDto Serial(CkCompiledModelRoot model) =>
        Machine(model).Attributes!.Single(a => a.AttributeName == "SerialNumber");

    private static CkAttributeDto SerialDefinition(CkCompiledModelRoot model) =>
        SemVerTestModels.GetAttribute(model, "SerialNumber");

    [Fact]
    public void T1_OptionalAttributeAddedToAPublicType_IsMinor()
    {
        var current = Model();
        Machine(current).Attributes!.Add(new CkTypeAttributeDto { CkAttributeId = $"{M}/SerialNumber", AttributeName = "Second", IsOptional = true });

        Assert.Equal(CkSemVerLevel.Minor, Level(Model(), current));
    }

    [Fact]
    public void T2_ImplementedInterfaceAdded_IsMinor()
    {
        var baseline = Model();
        Machine(baseline).Implements = null;

        Assert.Equal(CkSemVerLevel.Minor, Level(baseline, Model()));
    }

    [Fact]
    public void T3_AttributeAssociationInterfaceOrMethodRemoved_IsMajor()
    {
        var current = Model();
        Machine(current).Attributes!.RemoveAt(1);
        Assert.Equal(CkSemVerLevel.Major, Level(Model(), current));

        current = Model();
        Machine(current).Associations = [];
        Assert.Equal(CkSemVerLevel.Major, Level(Model(), current));

        current = Model();
        Machine(current).Implements = null;
        Assert.Equal(CkSemVerLevel.Major, Level(Model(), current));

        current = Model();
        Machine(current).Methods!.RemoveAt(1);
        Assert.Equal(CkSemVerLevel.Major, Level(Model(), current));
    }

    [Fact]
    public void T4_BaseTypeChanged_IsMajor()
    {
        var current = Model();
        Machine(current).DerivedFromCkTypeId = "Base/Other";

        Assert.Equal(CkSemVerLevel.Major, Assert.Single(Classify(Model(), current)).Level);
    }

    [Fact]
    public void T5_AbstractOrFinalFalseToTrue_IsMajor_TrueToFalse_IsMinor()
    {
        var final = Model();
        Machine(final).IsFinal = true;
        var isAbstract = Model();
        Machine(isAbstract).IsAbstract = true;

        Assert.Equal(CkSemVerLevel.Major, Level(Model(), final));
        Assert.Equal(CkSemVerLevel.Major, Level(Model(), isAbstract));
        Assert.Equal(CkSemVerLevel.Minor, Level(final, Model()));
        Assert.Equal(CkSemVerLevel.Minor, Level(isAbstract, Model()));
    }

    [Fact]
    public void T6_DerivableAnyToModel_IsMajor_ModelToAny_IsMinor()
    {
        var restricted = Model();
        Machine(restricted).Derivable = CkDerivableDto.Model;

        Assert.Equal(CkSemVerLevel.Major, Level(Model(), restricted));
        Assert.Equal(CkSemVerLevel.Minor, Level(restricted, Model()));
    }

    [Theory]
    [InlineData(null, CkAttributeAccessDto.ReadOnly, CkSemVerLevel.Major)]
    [InlineData(CkAttributeAccessDto.ReadOnly, CkAttributeAccessDto.MethodOnly, CkSemVerLevel.Major)]
    [InlineData(CkAttributeAccessDto.MethodOnly, CkAttributeAccessDto.Hidden, CkSemVerLevel.Major)]
    [InlineData(CkAttributeAccessDto.Hidden, CkAttributeAccessDto.ReadOnly, CkSemVerLevel.Minor)]
    [InlineData(CkAttributeAccessDto.ReadOnly, null, CkSemVerLevel.Minor)]
    public void T7_AccessTightened_IsMajor_Relaxed_IsMinor(CkAttributeAccessDto? before, CkAttributeAccessDto? after,
        CkSemVerLevel expected)
    {
        var baseline = Model();
        Serial(baseline).Access = before;
        var current = Model();
        Serial(current).Access = after;

        var change = Assert.Single(Classify(baseline, current));
        Assert.Equal(expected, change.Level);
        Assert.False(change.RequiresAcknowledge);
        if (expected == CkSemVerLevel.Major)
        {
            Assert.Contains("generic GraphQL clients and dependents lose write/read access", change.Reason);
        }
    }

    [Fact]
    public void T7_AccessTightenedOnASecuritySensitiveAttribute_IsMinorAndRequiresAcknowledge()
    {
        var baseline = Model();
        SerialDefinition(baseline).SecuritySensitive = true;
        var current = Model();
        SerialDefinition(current).SecuritySensitive = true;
        Serial(current).Access = CkAttributeAccessDto.Hidden;

        var change = Assert.Single(Classify(baseline, current));
        Assert.Equal(CkSemVerLevel.Minor, change.Level);
        Assert.True(change.RequiresAcknowledge);
        Assert.Contains("security exception", change.Reason);

        // Marked in the same release: the exception needs the flag in both versions.
        var classified = Classify(Model(), current);
        Assert.Contains(classified, c => c.Change.Property == "access" && c.Level == CkSemVerLevel.Major && !c.RequiresAcknowledge);

        // Any other change of a security-sensitive attribute follows the normal rules.
        var retyped = Model();
        SerialDefinition(retyped).SecuritySensitive = true;
        SerialDefinition(retyped).ValueType = AttributeValueTypesDto.Int;
        Assert.Equal(CkSemVerLevel.Major, Level(baseline, retyped));
    }

    private static IReadOnlyList<CkClassifiedModelChange> ClassifyWithHistory(CkCompiledModelRoot baseline,
        CkCompiledModelRoot current, params CkCompiledModelRoot[] earlierInMajor)
    {
        var changes = new Engine.SemVer.CkModelDiffService().Diff(baseline, current);
        return new Engine.SemVer.CkSemVerClassifier().Classify(changes, baseline, current, earlierInMajor);
    }

    private static CkCompiledModelRoot Sensitive(bool sensitive, string version, bool hasAttribute = true)
    {
        var model = Model();
        model.ModelId = new CkModelId(M, version);
        SerialDefinition(model).SecuritySensitive = sensitive;
        if (!hasAttribute)
        {
            Machine(model).Attributes!.RemoveAll(a => a.AttributeName == "SerialNumber");
        }

        return model;
    }

    [Fact]
    public void T7_AccessTightenedOnAnAttributeThatBecameSensitiveWithinTheMajor_IsMajorAndCannotBeAcknowledged()
    {
        // 1.0.0 not sensitive, 1.1.0 sensitive (row A3, Minor), 1.2.0 tightens: judged against the major line.
        var baseline = Sensitive(true, "1.1.0");
        var current = Sensitive(true, "1.2.0");
        Serial(current).Access = CkAttributeAccessDto.Hidden;

        var change = Assert.Single(ClassifyWithHistory(baseline, current, Sensitive(false, "1.0.0")));
        Assert.Equal(CkSemVerLevel.Major, change.Level);
        Assert.False(change.RequiresAcknowledge);
        Assert.Contains("became securitySensitive within this major line", change.Reason);
        Assert.Contains("not sensitive in 1.0.0", change.Reason);

        // The version of an earlier release does not matter, any of them counts (order of the input is irrelevant).
        var viaLater = Assert.Single(ClassifyWithHistory(baseline, current, Sensitive(true, "1.0.0"),
            Sensitive(false, "1.0.5")));
        Assert.Equal(CkSemVerLevel.Major, viaLater.Level);
    }

    [Fact]
    public void T7_SecurityException_HoldsWhenTheAttributeWasSensitiveInEveryEarlierVersion_OrBornSensitive()
    {
        var baseline = Sensitive(true, "1.2.0");
        var current = Sensitive(true, "1.3.0");
        Serial(current).Access = CkAttributeAccessDto.Hidden;

        var always = Assert.Single(ClassifyWithHistory(baseline, current, Sensitive(true, "1.0.0"), Sensitive(true, "1.1.0")));
        Assert.Equal(CkSemVerLevel.Minor, always.Level);
        Assert.True(always.RequiresAcknowledge);

        // Born sensitive in 1.1.0 (no assignment in 1.0.0): nothing contradicts the marker.
        var born = Assert.Single(ClassifyWithHistory(baseline, current, Sensitive(true, "1.0.0", hasAttribute: false),
            Sensitive(true, "1.1.0")));
        Assert.Equal(CkSemVerLevel.Minor, born.Level);
        Assert.True(born.RequiresAcknowledge);

        // Born non-sensitive in 1.1.0, marked in 1.2.0: the first version that has it says no.
        var bornPlain = Assert.Single(ClassifyWithHistory(baseline, current, Sensitive(true, "1.0.0", hasAttribute: false),
            Sensitive(false, "1.1.0")));
        Assert.Equal(CkSemVerLevel.Major, bornPlain.Level);
    }

    private static CkCompiledModelRoot FirstRelease(string version, int ckLanguage, bool sensitive,
        CkAttributeAccessDto? access = null)
    {
        var model = Sensitive(sensitive, version);
        model.CkLanguage = ckLanguage;
        Serial(model).Access = access;
        return model;
    }

    [Fact]
    public void T7a_FirstCkLanguage2Release_TighteningAMarkedAttribute_IsMinorAndRequiresAcknowledge()
    {
        // AB#7771 (G1 = B): baseline classic (cannot carry the marker), candidate ckLanguage 2 marks and hides.
        var baseline = FirstRelease("1.0.0", 1, false);
        var current = FirstRelease("1.1.0", 2, true, CkAttributeAccessDto.Hidden);

        var change = Assert.Single(Classify(baseline, current), c => c.Change.Property == "access");
        Assert.Equal(CkSemVerLevel.Minor, change.Level);
        Assert.True(change.RequiresAcknowledge);
        Assert.Contains("first ckLanguage 2 release", change.Reason);

        // With history (all classic): same verdict.
        var withHistory = Assert.Single(ClassifyWithHistory(baseline, current, FirstRelease("1.0.0", 1, false)),
            c => c.Change.Property == "access");
        Assert.Equal(CkSemVerLevel.Minor, withHistory.Level);
        Assert.True(withHistory.RequiresAcknowledge);

        // The history is requested so the exception can be checked against it.
        var diff = new Engine.SemVer.CkModelDiffService().Diff(baseline, current);
        Assert.True(new Engine.SemVer.CkSemVerClassifier().NeedsMajorLineHistory(diff, baseline, current));
    }

    [Fact]
    public void T7a_FirstCkLanguage2Release_AnAttributeNotMarkedSensitive_StaysMajor()
    {
        var baseline = FirstRelease("1.0.0", 1, false);
        var current = FirstRelease("1.1.0", 2, false, CkAttributeAccessDto.Hidden);

        var change = Assert.Single(Classify(baseline, current), c => c.Change.Property == "access");
        Assert.Equal(CkSemVerLevel.Major, change.Level);
        Assert.False(change.RequiresAcknowledge);
    }

    [Fact]
    public void T7a_SecondCkLanguage2Release_CannotUseTheExceptionAgain()
    {
        // Baseline is already ckLanguage 2: marking and hiding in one release is a Major.
        var baseline = FirstRelease("1.1.0", 2, false);
        var current = FirstRelease("1.2.0", 2, true, CkAttributeAccessDto.Hidden);

        var change = Assert.Single(Classify(baseline, current), c => c.Change.Property == "access");
        Assert.Equal(CkSemVerLevel.Major, change.Level);
        Assert.False(change.RequiresAcknowledge);

        // Defensive: a ckLanguage 2 version anywhere earlier in the major also closes the exception.
        var classicBaseline = FirstRelease("1.2.0", 1, false);
        var viaHistory = Assert.Single(ClassifyWithHistory(classicBaseline, current, FirstRelease("1.0.0", 2, false)),
            c => c.Change.Property == "access");
        Assert.Equal(CkSemVerLevel.Major, viaHistory.Level);
    }

    [Fact]
    public void T7a_ClassicToClassic_IsNotAFirstCkLanguage2Release()
    {
        var baseline = FirstRelease("1.0.0", 1, false);
        var current = FirstRelease("1.1.0", 1, true, CkAttributeAccessDto.Hidden);

        Assert.DoesNotContain(Classify(baseline, current),
            c => c is { Level: CkSemVerLevel.Minor, RequiresAcknowledge: true });
    }

    [Fact]
    public void T7_NeedsMajorLineHistory_OnlyForAnAccessTighteningOnASensitiveAttribute()
    {
        var classifier = new Engine.SemVer.CkSemVerClassifier();
        var diff = new Engine.SemVer.CkModelDiffService();
        var baseline = Sensitive(true, "1.0.0");
        var tightened = Sensitive(true, "1.1.0");
        Serial(tightened).Access = CkAttributeAccessDto.Hidden;
        var plainBaseline = Sensitive(false, "1.0.0");
        var plainTightened = Sensitive(false, "1.1.0");
        Serial(plainTightened).Access = CkAttributeAccessDto.Hidden;

        Assert.True(classifier.NeedsMajorLineHistory(diff.Diff(baseline, tightened), baseline, tightened));
        Assert.False(classifier.NeedsMajorLineHistory(diff.Diff(plainBaseline, plainTightened), plainBaseline, plainTightened));
        Assert.False(classifier.NeedsMajorLineHistory(diff.Diff(baseline, baseline), baseline, baseline));
    }

    [Fact]
    public void E1_EnumValueAdded_IsMinor_AlsoForANonExtensibleEnum()
    {
        var current = Model();
        SemVerTestModels.GetEnum(current).Values.Add(new CkEnumValueDto { Key = 2, Name = "Standby" });

        Assert.False(SemVerTestModels.GetEnum(current).IsExtensible);
        Assert.Equal(CkSemVerLevel.Minor, Level(Model(), current));
    }

    [Fact]
    public void E2_EnumValueRemovedOrRenumbered_UseFlagsChanged_ExtensibleRevoked_IsMajor()
    {
        var removed = Model();
        SemVerTestModels.GetEnum(removed).Values.Remove(SemVerTestModels.GetEnum(removed).Values.First());
        var renumbered = Model();
        SemVerTestModels.GetEnum(renumbered).Values.First().Key = 7;
        var flags = Model();
        SemVerTestModels.GetEnum(flags).UseFlags = true;
        var extensible = Model();
        SemVerTestModels.GetEnum(extensible).IsExtensible = true;

        Assert.Equal(CkSemVerLevel.Major, Level(Model(), removed));
        Assert.Equal(CkSemVerLevel.Major, Level(Model(), renumbered));
        Assert.Equal(CkSemVerLevel.Major, Level(Model(), flags));
        Assert.Equal(CkSemVerLevel.Major, Level(extensible, Model()));
    }

    [Fact]
    public void R1_OptionalRecordAttributeAdded_IsMinor()
    {
        var current = Model();
        SemVerTestModels.GetRecord(current).Attributes!.Add(new CkTypeAttributeDto
        {
            CkAttributeId = $"{M}/SerialNumber", AttributeName = "Zip", IsOptional = true
        });

        Assert.Equal(CkSemVerLevel.Minor, Level(Model(), current));
    }

    [Fact]
    public void R2_RecordAttributeRemovedRetargetedOrMadeRequired_IsMajor()
    {
        var removed = Model();
        SemVerTestModels.GetRecord(removed).Attributes!.Clear();
        var retargeted = Model();
        SemVerTestModels.GetRecord(retargeted).Attributes![0].CkAttributeId = $"{M}/WithDefault";
        var required = Model();
        SemVerTestModels.GetRecord(required).Attributes![0].IsOptional = false;

        Assert.Equal(CkSemVerLevel.Major, Level(Model(), removed));
        Assert.Equal(CkSemVerLevel.Major, Level(Model(), retargeted));
        Assert.Equal(CkSemVerLevel.Major, Level(Model(), required));
    }

    [Fact]
    public void A1_AttributeDescription_IsPatch_MetaData_IsMinor()
    {
        var described = Model();
        SerialDefinition(described).Description = "Other";
        var meta = Model();
        SerialDefinition(meta).MetaData = [new CkAttributeMetaDataDto { Key = "unit", Value = "mm" }];

        Assert.Equal(CkSemVerLevel.Patch, Level(Model(), described));
        Assert.Equal(CkSemVerLevel.Minor, Level(Model(), meta));
    }

    [Fact]
    public void A2_ValueTypeRecordOrEnumIdChanged_IsMajor_StringToSecret_IsMinor()
    {
        var retyped = Model();
        SerialDefinition(retyped).ValueType = AttributeValueTypesDto.Int;
        var reEnumed = Model();
        SemVerTestModels.GetAttribute(reEnumed, "StateAttr").ValueCkEnumId = "Base/Other";
        var secret = Model();
        SerialDefinition(secret).ValueType = AttributeValueTypesDto.Secret;

        Assert.Equal(CkSemVerLevel.Major, Level(Model(), retyped));
        Assert.Equal(CkSemVerLevel.Major, Level(Model(), reEnumed));
        Assert.Equal(CkSemVerLevel.Minor, Level(Model(), secret));
    }

    [Fact]
    public void A3_SecuritySensitiveSetOrCleared_IsMinor()
    {
        var marked = Model();
        SerialDefinition(marked).SecuritySensitive = true;
        var explicitFalse = Model();
        SerialDefinition(explicitFalse).SecuritySensitive = false;

        var change = Assert.Single(Classify(Model(), marked));
        Assert.Equal("securitySensitive", change.Change.Property);
        Assert.Equal(CkSemVerLevel.Minor, change.Level);
        Assert.Equal(CkSemVerLevel.Minor, Level(marked, Model()));
        Assert.Empty(Classify(Model(), explicitFalse));
    }

    /// <summary>
    ///     T1–T7 also hold for the v1 stable bases <c>System/Entity</c> and <c>System/Configuration</c>.
    /// </summary>
    [Theory]
    [InlineData("Entity")]
    [InlineData("Configuration")]
    public void StableBases_FollowTheTypeRows(string typeName)
    {
        CkCompiledModelRoot System()
        {
            var model = SemVerTestModels.CreateModel();
            model.ModelId = new CkModelId("System", "2.5.0");
            model.Types!.Single().TypeId = typeName;
            model.Types!.Single().DerivedFromCkTypeId = null;
            return model;
        }

        var optional = System();
        optional.Types!.Single().Attributes!.Add(new CkTypeAttributeDto { CkAttributeId = "System/SerialNumber", AttributeName = "Extra", IsOptional = true });
        var removed = System();
        removed.Types!.Single().Attributes!.RemoveAt(1);
        var final = System();
        final.Types!.Single().IsFinal = true;
        var tightened = System();
        tightened.Types!.Single().Attributes![0].Access = CkAttributeAccessDto.ReadOnly;

        var system = System();
        Assert.True(Engine.SemVer.CkSemVerClassifier.IsStableBase(system, system.Types!.Single().TypeId.FullName));
        Assert.Equal(CkSemVerLevel.Minor, Level(System(), optional));
        Assert.Equal(CkSemVerLevel.Major, Level(System(), removed));
        Assert.Equal(CkSemVerLevel.Major, Level(System(), final));
        Assert.Equal(CkSemVerLevel.Major, Level(System(), tightened));
    }
}
