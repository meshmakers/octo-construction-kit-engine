using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using static Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer.Rows.RowTestSupport;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer.Rows;

/// <summary>
///     AB#6270 rows B1–B4: behavioural changes and unique indexes on stable bases are flagged separately.
/// </summary>
public class BehaviouralRowTests
{
    private static CkTypeIndexDto UniqueIndex() => new()
    {
        IndexType = IndexTypeDto.Unique, Fields = [new CkIndexFieldsDto { AttributePaths = ["serialNumber"] }]
    };

    [Fact]
    public void B1_DefaultsDisplayRulesAutoValuesChangeStreamsAndTimeouts_AreBehaviouralWithUnchangedLevel()
    {
        var cases = new (Action<CkCompiledModelRoot> Change, CkSemVerLevel Level)[]
        {
            (m => SemVerTestModels.GetAttribute(m, "WithDefault").DefaultValues = [7], CkSemVerLevel.Minor),
            (m => Machine(m).DisplayNameRule = "{serialNumber}", CkSemVerLevel.Patch),
            (m => Machine(m).DisplayDescriptionRule = "{serialNumber}", CkSemVerLevel.Patch),
            (m => Machine(m).Attributes![0].AutoCompleteValues = ["a"], CkSemVerLevel.Minor),
            (m => Machine(m).Attributes![0].AutoIncrementReference = "counter", CkSemVerLevel.Minor),
            (m => Machine(m).EnableChangeStreamPreAndPostImages = true, CkSemVerLevel.Minor),
            (m => Method(m, "type").Execution!.TimeoutSeconds = 60, CkSemVerLevel.Minor)
        };

        foreach (var (change, level) in cases)
        {
            var current = Model();
            change(current);
            var classified = Classify(Model(), current).Where(c => c.Change.Property != "signature").ToList();

            var single = Assert.Single(classified);
            Assert.True(single.IsBehavioural, single.Change.Property);
            Assert.False(single.RequiresAcknowledge);
            Assert.Equal(level, single.Level);
        }
    }

    [Fact]
    public void B1_DefaultRemovedFromARequiredOrPublicDefinition_IsMajor()
    {
        // AB#6341 (gate case 7u): the public definition WithDefault, default removed — a dependent model may assign
        // it as required.
        var current = Model();
        SemVerTestModels.GetAttribute(current, "WithDefault").DefaultValues = null;
        var change = Assert.Single(Classify(Model(), current));
        Assert.Equal(CkSemVerLevel.Major, change.Level);
        Assert.True(change.IsBehavioural);
        Assert.Contains("major release", change.Reason);

        // Two-step: 1.0.0 -> 1.1.0 adds a required attribute WITH default (Minor), 1.1.0 -> 1.2.0 removes the default
        // (Major) — both steps are visible, the second is not a silent Minor.
        static CkCompiledModelRoot WithRequiredInternalCount(bool withDefault)
        {
            var model = Model();
            model.Attributes!.Add(new CkAttributeDto
            {
                AttributeId = "Count", ValueType = AttributeValueTypesDto.Int,
                Visibility = CkVisibilityDto.Internal, DefaultValues = withDefault ? [1] : null
            });
            Machine(model).Attributes!.Add(new CkTypeAttributeDto { CkAttributeId = $"{M}/Count", AttributeName = "Count" });
            return model;
        }

        Assert.Equal(CkSemVerLevel.Minor, Level(Model(), WithRequiredInternalCount(true)));
        Assert.Equal(CkSemVerLevel.Major, Level(WithRequiredInternalCount(true), WithRequiredInternalCount(false)));
    }

    [Fact]
    public void B1_DefaultRemovedFromAnInternalOptionalDefinition_StaysMinorAndBehavioural()
    {
        static CkCompiledModelRoot WithOptionalInternalCount(bool withDefault)
        {
            var model = Model();
            model.Attributes!.Add(new CkAttributeDto
            {
                AttributeId = "Count", ValueType = AttributeValueTypesDto.Int,
                Visibility = CkVisibilityDto.Internal, DefaultValues = withDefault ? [1] : null
            });
            Machine(model).Attributes!.Add(new CkTypeAttributeDto
            {
                CkAttributeId = $"{M}/Count", AttributeName = "Count", IsOptional = true
            });
            return model;
        }

        var change = Assert.Single(Classify(WithOptionalInternalCount(true), WithOptionalInternalCount(false)));
        Assert.Equal(CkSemVerLevel.Minor, change.Level);
        Assert.True(change.IsBehavioural);

        // A default changed to another value stays Minor; a ckLanguage 1 model keeps Minor (no v1 level change).
        var changed = Model();
        SemVerTestModels.GetAttribute(changed, "WithDefault").DefaultValues = [7];
        Assert.Equal(CkSemVerLevel.Minor, Level(Model(), changed));
        var v1Baseline = SemVerTestModels.CreateModel();
        var v1Current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetAttribute(v1Current, "WithDefault").DefaultValues = null;
        Assert.Equal(CkSemVerLevel.Minor, Level(v1Baseline, v1Current));
    }

    [Fact]
    public void B2_NonUniqueIndexAddedOrRemoved_IsMinorAndBehavioural()
    {
        var current = Model();
        Machine(current).Indexes!.Add(new CkTypeIndexDto
        {
            IndexType = IndexTypeDto.Ascending, Fields = [new CkIndexFieldsDto { AttributePaths = ["state"] }]
        });

        var added = Assert.Single(Classify(Model(), current));
        Assert.Equal(CkSemVerLevel.Minor, added.Level);
        Assert.True(added.IsBehavioural);

        var removed = Assert.Single(Classify(current, Model()));
        Assert.Equal(CkSemVerLevel.Minor, removed.Level);
        Assert.True(removed.IsBehavioural);
    }

    [Fact]
    public void B3_UniqueIndexOnANonStableType_IsMajorWithoutMarker()
    {
        CkCompiledModelRoot Final()
        {
            var model = Model();
            Machine(model).IsFinal = true;
            return model;
        }

        var current = Final();
        Machine(current).Indexes!.Add(UniqueIndex());

        var change = Assert.Single(Classify(Final(), current));
        Assert.Equal(CkSemVerLevel.Major, change.Level);
        Assert.False(change.RequiresAcknowledge);
        Assert.False(change.IsBehavioural);
    }

    [Fact]
    public void B4_UniqueIndexOnAStableBase_IsMajorAndRequiresAcknowledge()
    {
        // ckLanguage 2: public, not final, derivable: Any
        var current = Model();
        Machine(current).Indexes!.Add(UniqueIndex());
        var change = Assert.Single(Classify(Model(), current));
        Assert.Equal(CkSemVerLevel.Major, change.Level);
        Assert.True(change.RequiresAcknowledge);
        Assert.Contains("derived type", change.Reason);

        // ckLanguage 1: System/Entity
        CkCompiledModelRoot System()
        {
            var model = SemVerTestModels.CreateModel();
            model.ModelId = new CkModelId("System", "2.5.0");
            model.Types!.Single().TypeId = "Entity";
            return model;
        }

        var v1 = System();
        v1.Types!.Single().Indexes!.Add(UniqueIndex());
        var v1Change = Assert.Single(Classify(System(), v1));
        Assert.Equal(CkSemVerLevel.Major, v1Change.Level);
        Assert.True(v1Change.RequiresAcknowledge);
    }

    [Theory]
    [InlineData(2, null, false, false, true)]
    [InlineData(2, CkVisibilityDto.Internal, false, false, false)]
    [InlineData(2, null, true, false, false)]
    [InlineData(2, null, false, true, false)]
    [InlineData(1, null, false, false, false)]
    public void StableBasePredicate_CoversV1AndV2(int ckLanguage, CkVisibilityDto? visibility, bool isFinal,
        bool derivableModel, bool expected)
    {
        var model = ckLanguage == 2 ? Model() : SemVerTestModels.CreateModel();
        Machine(model).Visibility = visibility;
        Machine(model).IsFinal = isFinal;
        Machine(model).Derivable = derivableModel ? CkDerivableDto.Model : ckLanguage == 2 ? CkDerivableDto.Any : null;

        Assert.Equal(expected, CkSemVerClassifier.IsStableBase(model, Machine(model).TypeId.FullName));
    }

    [Fact]
    public void BehaviouralChanges_HaveTheirOwnChangelogSection()
    {
        var current = Model();
        SemVerTestModels.GetAttribute(current, "WithDefault").DefaultValues = [7];
        Machine(current).Attributes!.Add(new CkTypeAttributeDto { CkAttributeId = $"{M}/SerialNumber", AttributeName = "Extra", IsOptional = true });
        var classified = Classify(Model(), current);

        var changelog = new CkChangelogGenerator().Generate(null, new CkVersion("1.1.0"), new DateTime(2026, 10, 10),
            CkSemVerLevel.Minor, classified, null);

        Assert.Contains("### Added", changelog);
        Assert.Contains("### Behavioural changes", changelog);
        var behaviouralSection = changelog.Substring(changelog.IndexOf("### Behavioural changes", StringComparison.Ordinal));
        Assert.Contains("defaultValues", behaviouralSection);
        Assert.DoesNotContain("Extra", behaviouralSection);
    }

    // ── AB#6339: indexes are paired by their (case-insensitive) field paths in ckLanguage 2 models ──

    private static CkCompiledModelRoot WithIndex(IndexTypeDto type, string path = "serialNumber", bool stableBase = false,
        int ckLanguage = 2)
    {
        var model = ckLanguage == 2 ? Model() : SemVerTestModels.CreateModel();
        Machine(model).IsFinal = !stableBase;
        Machine(model).Indexes =
        [
            new CkTypeIndexDto
            {
                IndexType = type,
                Fields = [new CkIndexFieldsDto { AttributePaths = [path] }, new CkIndexFieldsDto { AttributePaths = ["rtWellKnownName"] }]
            }
        ];
        return model;
    }

    [Fact]
    public void B3_IndexPathChangedOnlyInCase_IsNoChange()
    {
        // Gate cases 7r (unique) and 6a (non-unique): paths resolve case-insensitively.
        Assert.Empty(Classify(WithIndex(IndexTypeDto.Unique), WithIndex(IndexTypeDto.Unique, "SerialNumber")));
        Assert.Empty(Classify(WithIndex(IndexTypeDto.Ascending), WithIndex(IndexTypeDto.Ascending, "SERIALNUMBER")));

        // A ckLanguage 1 model keeps the v1 verdict (no v1 level changes): remove + add, the unique one is Major.
        Assert.Equal(CkSemVerLevel.Major,
            Level(WithIndex(IndexTypeDto.Unique, ckLanguage: 1), WithIndex(IndexTypeDto.Unique, "SerialNumber", ckLanguage: 1)));
    }

    [Theory]
    [InlineData(IndexTypeDto.Unique, IndexTypeDto.UniqueNotDeleted, CkSemVerLevel.Minor)] // gate case 7p
    [InlineData(IndexTypeDto.UniqueNotDeleted, IndexTypeDto.Unique, CkSemVerLevel.Major)]
    [InlineData(IndexTypeDto.Ascending, IndexTypeDto.Unique, CkSemVerLevel.Major)]
    [InlineData(IndexTypeDto.Ascending, IndexTypeDto.UniqueNotDeleted, CkSemVerLevel.Major)]
    [InlineData(IndexTypeDto.Unique, IndexTypeDto.Ascending, CkSemVerLevel.Minor)]
    [InlineData(IndexTypeDto.Ascending, IndexTypeDto.Text, CkSemVerLevel.Minor)]
    public void B3_IndexTypeChanged_IsOneModification(IndexTypeDto before, IndexTypeDto after, CkSemVerLevel expected)
    {
        var change = Assert.Single(Classify(WithIndex(before), WithIndex(after)));
        Assert.Equal(CkModelChangeKind.Modified, change.Change.ChangeKind);
        Assert.Equal("indexType", change.Change.Property);
        Assert.Equal(expected, change.Level);
        Assert.Equal(expected == CkSemVerLevel.Minor, change.IsBehavioural);
        Assert.False(change.RequiresAcknowledge);
    }

    [Fact]
    public void B4_UniqueNotDeletedToUniqueOnAStableBase_IsMajorWithAcknowledge()
    {
        var change = Assert.Single(Classify(WithIndex(IndexTypeDto.UniqueNotDeleted, stableBase: true),
            WithIndex(IndexTypeDto.Unique, stableBase: true)));
        Assert.Equal(CkSemVerLevel.Major, change.Level);
        Assert.True(change.RequiresAcknowledge);

        // The relaxation on a stable base needs no acknowledge.
        var relaxed = Assert.Single(Classify(WithIndex(IndexTypeDto.Unique, stableBase: true),
            WithIndex(IndexTypeDto.UniqueNotDeleted, stableBase: true)));
        Assert.Equal(CkSemVerLevel.Minor, relaxed.Level);
        Assert.False(relaxed.RequiresAcknowledge);
    }

    [Fact]
    public void B3_CompoundUniqueKeyLosesAField_StaysMajor()
    {
        // Gate control 7q: a different field list is not a pair; the new (narrower) unique index is Major.
        var current = WithIndex(IndexTypeDto.Unique);
        Machine(current).Indexes![0].Fields = [new CkIndexFieldsDto { AttributePaths = ["serialNumber"] }];
        Assert.Equal(CkSemVerLevel.Major, Level(WithIndex(IndexTypeDto.Unique), current));
    }
}
