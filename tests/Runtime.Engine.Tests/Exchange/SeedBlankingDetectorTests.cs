using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Meshmakers.Octo.Runtime.Contracts.Exchange;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Meshmakers.Octo.Runtime.Engine.Blueprints;
using Meshmakers.Octo.Runtime.Engine.Exchange;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Exchange;

/// <summary>
///     AB#6315: the update preview and the import share one detection (<see cref="SeedBlankingDetector" />),
///     the confirmation reaches exactly the confirmed entity/attribute, and summaries never carry values.
/// </summary>
public class SeedBlankingDetectorTests
{
    private const string Secret = "Passw0rd-Pon7on-Secret";

    private static readonly string FilledConfiguration =
        $$$"""{"EdaHttpAdapter":{"Host":"http://ponton.internal","User":"edauser","Password":"{{{Secret}}}"}}""";

    private const string EmptySkeleton = """{"EdaHttpAdapter":{"Host":"","User":"","Password":""}}""";

    private static readonly CkTypeAttributeGraph[] Attrs =
    [
        TypeAttr("Configuration", AttributeValueTypesDto.String, AttributeOwnershipDto.SeedOwned),
        TypeAttr("Comment", AttributeValueTypesDto.String, AttributeOwnershipDto.SeedOwned),
        TypeAttr("Tenant", AttributeValueTypesDto.String, AttributeOwnershipDto.TenantOwned),
    ];

    [Fact]
    public void Detect_FindsBlankedSeedOwnedAttributes_AndIgnoresOthers()
    {
        var existing = Existing(("Configuration", FilledConfiguration), ("Comment", "c"), ("Tenant", "t"));
        var seed = Seed(existing.RtId, ("Configuration", EmptySkeleton), ("Comment", "new"), ("Tenant", ""));

        var findings = SeedBlankingDetector.Detect(seed, existing, Attrs);

        var finding = Assert.Single(findings);
        Assert.Equal("Configuration", finding.Attribute.AttributeName);
        Assert.Equal(RtImportBlankingReason.SeedEmpty, finding.Reason);
    }

    [Fact]
    public void PreviewDetection_AndImportGuard_AgreeOnTheSameInput()
    {
        var existing = Existing(("Configuration", FilledConfiguration), ("Comment", "c"));
        var detectSeed = Seed(existing.RtId, ("Configuration", EmptySkeleton));
        var guardSeed = Seed(existing.RtId, ("Configuration", EmptySkeleton));

        var preview = SeedBlankingDetector.Detect(detectSeed, existing, Attrs)
            .Select(f => (f.Attribute.AttributeName, f.Reason)).OrderBy(x => x.AttributeName).ToList();
        var apply = ImportRtModelCommand.GuardSeedOwnedAttributesForEntity(guardSeed, existing, Attrs, v => v,
                RtImportBlankingPolicy.Keep)
            .Select(e => (e.AttributeName, e.Reason)).OrderBy(x => x.AttributeName).ToList();

        Assert.Equal(2, preview.Count); // Configuration (empty skeleton) and Comment (omitted)
        Assert.Equal(preview, apply);
    }

    [Fact]
    public void Guard_ConfirmationForExactlyOnePair_BlanksOnlyThatOne()
    {
        var existing = Existing(("Configuration", FilledConfiguration), ("Comment", "c"));
        var model = Seed(existing.RtId, ("Configuration", EmptySkeleton), ("Comment", ""));
        var confirmed = new[] { new RtImportBlankingConfirmation(existing.RtId, "configuration") };

        var entries = ImportRtModelCommand.GuardSeedOwnedAttributesForEntity(model, existing, Attrs, v => v,
            RtImportBlankingPolicy.Keep, confirmed);

        Assert.Equal(2, entries.Count);
        Assert.True(entries.Single(e => e.AttributeName == "Configuration").Applied);
        Assert.False(entries.Single(e => e.AttributeName == "Comment").Applied);
        Assert.Equal(EmptySkeleton, ValueOf(model, "Configuration"));
        Assert.Equal("c", ValueOf(model, "Comment"));
    }

    [Fact]
    public void Guard_ConfirmationForAnotherEntity_DoesNotApply()
    {
        var existing = Existing(("Configuration", FilledConfiguration));
        var model = Seed(existing.RtId, ("Configuration", EmptySkeleton));
        var confirmed = new[] { new RtImportBlankingConfirmation(OctoObjectId.GenerateNewId(), "Configuration") };

        var entries = ImportRtModelCommand.GuardSeedOwnedAttributesForEntity(model, existing, Attrs, v => v,
            RtImportBlankingPolicy.Keep, confirmed);

        Assert.False(Assert.Single(entries).Applied);
        Assert.Equal(FilledConfiguration, ValueOf(model, "Configuration"));
    }

    [Fact]
    public void Summaries_DescribeKindAndSize_NeverTheValue()
    {
        Assert.Equal($"string ({FilledConfiguration.Length} chars)", SeedBlankingDetector.Summarize(FilledConfiguration));
        Assert.Equal("empty string", SeedBlankingDetector.Summarize(""));
        Assert.Equal("null", SeedBlankingDetector.Summarize(null));
        Assert.Equal("omitted", SeedBlankingDetector.Summarize(null, present: false));
        Assert.Equal("array (2 items)", SeedBlankingDetector.Summarize(new List<string> { "a", "b" }));
        Assert.DoesNotContain(Secret, SeedBlankingDetector.Summarize(FilledConfiguration));
    }

    // ---- BlueprintService: option -> engine confirmation, result report --------------------------------

    [Fact]
    public void Options_DefaultKeeps_AllowBlankingNeedsNoPairs_PairsAreParsed()
    {
        var result = new BlueprintUpdateResult();
        var id = OctoObjectId.GenerateNewId();

        Assert.False(new BlueprintUpdateOptions().AllowBlanking);
        Assert.Null(BlueprintService.ToImportConfirmations(new BlueprintUpdateOptions(), result));
        Assert.Null(BlueprintService.ToImportConfirmations(
            new BlueprintUpdateOptions { AllowBlanking = true }, result));

        var confirmations = BlueprintService.ToImportConfirmations(new BlueprintUpdateOptions
        {
            ConfirmedBlankings =
            [
                new BlueprintBlankingConfirmation { RtId = id.ToString()!, AttributeName = "Configuration" },
                new BlueprintBlankingConfirmation { RtId = "not-an-id", AttributeName = "Configuration" }
            ]
        }, result);

        var parsed = Assert.Single(confirmations!);
        Assert.Equal(id, parsed.RtId);
        Assert.Single(result.Warnings); // the invalid one is ignored, never widened
    }

    [Fact]
    public void Result_ListsKeptAndAppliedAttributes_FromTheDiff()
    {
        var kept = Blanked("aaaaaaaaaaaaaaaaaaaaaaaa", "Configuration");
        var applied = Blanked("bbbbbbbbbbbbbbbbbbbbbbbb", "Configuration");
        var options = new BlueprintUpdateOptions
        {
            ConfirmedBlankings = [new BlueprintBlankingConfirmation { RtId = applied.RtId, AttributeName = "configuration" }]
        };
        var result = new BlueprintUpdateResult();
        var confirmations = BlueprintService.ToImportConfirmations(options, result);

        CreateService().ReportBlankedAttributes([kept, applied], options, confirmations, result);

        Assert.False(result.BlankedAttributes.Single(b => b.RtId == kept.RtId).AppliedOnUpdate);
        Assert.True(result.BlankedAttributes.Single(b => b.RtId == applied.RtId).AppliedOnUpdate);
        Assert.Contains(result.Warnings, w => w.Contains("1 attribute(s) were kept"));
    }

    [Fact]
    public void Result_AllowBlanking_MarksEverythingApplied_AndAddsGuardEntriesBeyondTheDiff()
    {
        var command = A.Fake<IImportRtModelCommand>();
        var extra = new RtImportGuardEntry(OctoObjectId.GenerateNewId(), new RtCkId<CkTypeId>("Test/Type"),
            "Other", RtImportBlankingReason.SeedOmitted, true);
        A.CallTo(() => command.GuardEntries).Returns([extra]);
        var options = new BlueprintUpdateOptions { AllowBlanking = true };
        var result = new BlueprintUpdateResult();

        CreateService(command).ReportBlankedAttributes([Blanked("aaaaaaaaaaaaaaaaaaaaaaaa", "Configuration")],
            options, null, result);

        Assert.Equal(2, result.BlankedAttributes.Count);
        Assert.All(result.BlankedAttributes, b => Assert.True(b.AppliedOnUpdate));
        Assert.Empty(result.Warnings);
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private static BlueprintBlankedAttribute Blanked(string rtId, string attribute) => new()
    {
        RtId = rtId,
        CkTypeId = "Test/Type",
        AttributeName = attribute,
        Reason = "SeedEmpty",
        CurrentSummary = "string (223 chars)",
        IncomingSummary = "string (110 chars)"
    };

    private static BlueprintService CreateService(IImportRtModelCommand? command = null) => new(
        A.Fake<ICkCacheService>(),
        A.Fake<Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs.IBlueprintCatalogManager>(),
        A.Fake<ITenantBlueprintHistory>(),
        A.Fake<IBlueprintMigrationExecutor>(),
        A.Fake<IBlueprintMigrationParser>(),
        A.Fake<Meshmakers.Octo.Runtime.Contracts.CkModelMigrations.ICkModelUpgradeService>(),
        A.Fake<IRuntimeRepositoryProvider>(),
        command ?? A.Fake<IImportRtModelCommand>(),
        A.Fake<IRtYamlSerializer>(),
        A.Fake<IBlueprintNotifications>(),
        A.Fake<IBlueprintDependencyResolver>(),
        A.Fake<ITenantBlueprintInstallations>(),
        A.Fake<IBlueprintVariableProvider>(),
        NullLogger<BlueprintService>.Instance);

    private static object? ValueOf(RtEntityTcDto model, string name) =>
        model.Attributes.Single(a => a.Id.ElementId.Name == name).Value;

    private static CkTypeAttributeGraph TypeAttr(string name, AttributeValueTypesDto valueType,
        AttributeOwnershipDto ownership)
    {
        var attrId = new CkId<CkAttributeId>($"Test-1.0.0/{name}");
        var ckAttrGraph = new CkAttributeGraph(attrId,
            new CkAttributeDto { AttributeId = name, ValueType = valueType, Ownership = ownership });
        return new CkTypeAttributeGraph(attrId,
            new CkTypeAttributeDto { CkAttributeId = attrId, AttributeName = name }, ckAttrGraph);
    }

    private static RtEntityTcDto Seed(OctoObjectId rtId, params (string Name, object? Value)[] attrs)
    {
        var entity = new RtEntityTcDto
        {
            RtId = rtId,
            CkTypeId = new RtCkId<CkTypeId>("Test/TestType"),
        };
        foreach (var (name, value) in attrs)
        {
            entity.Attributes.Add(new RtAttributeTcDto
            {
                Id = new RtCkId<CkAttributeId>($"Test/{name}"),
                Value = value,
            });
        }

        return entity;
    }

    private static RtEntity Existing(params (string Name, object? Value)[] attrs)
    {
        var entity = new RtEntity
        {
            RtId = OctoObjectId.GenerateNewId(),
            CkTypeId = new RtCkId<CkTypeId>("Test/TestType"),
        };
        foreach (var (name, value) in attrs)
        {
            entity.SetAttributeRawValue(name, value);
        }

        return entity;
    }
}
