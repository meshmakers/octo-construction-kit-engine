using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;
using Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.systemFake;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Resolvers;

/// <summary>
///     Display rule inheritance (nearest non-empty rule wins along the derivedFromCkTypeId chain)
///     and compile-time validation (syntax, unknown attribute paths incl. record paths).
///     Message numbers: 67 = DisplayRuleSyntaxInvalid, 68 = DisplayRuleAttributePathUnknown.
/// </summary>
public class InheritanceResolverDisplayRuleTests
{
    private readonly ILoggerFactory _loggerFactory;

    public InheritanceResolverDisplayRuleTests(ITestOutputHelper output)
    {
        _loggerFactory = LoggerFactory.Create(builder => { builder.AddXUnit(output); });
    }

    private CkModelGraph Resolve(CkCompiledModelRoot sampleModel, OperationResult operationResult)
    {
        var logger = _loggerFactory.CreateLogger<InheritanceResolver>();

        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(Builder.Build());
        modelGraph.AppendModel(sampleModel);

        var originFileResolver = new OriginFileResolver("TEST");
        InheritanceResolver inheritanceResolver = new(logger);
        inheritanceResolver.Resolve(modelGraph, originFileResolver, operationResult);
        return modelGraph;
    }

    [Fact]
    public void DisplayRules_InheritedByDerivedTypes_NearestRuleWins()
    {
        var sampleModel = sampleData.sample1.Builder.Build();
        // Demo1 declares both rules; Demo2 overrides the name rule; Demo3 declares nothing.
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo1").DisplayNameRule = "${A}";
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo1").DisplayDescriptionRule = "${B} (${C})";
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo2").DisplayNameRule = "${D} ${A}";

        OperationResult operationResult = new();
        var modelGraph = Resolve(sampleModel, operationResult);

        Assert.Empty(operationResult.Messages);

        Assert.Equal("${A}", modelGraph.Types["sample1/Demo1"].DisplayNameRule);
        Assert.Equal("${B} (${C})", modelGraph.Types["sample1/Demo1"].DisplayDescriptionRule);

        // Demo2 overrides the name rule, inherits the description rule from Demo1
        Assert.Equal("${D} ${A}", modelGraph.Types["sample1/Demo2"].DisplayNameRule);
        Assert.Equal("${B} (${C})", modelGraph.Types["sample1/Demo2"].DisplayDescriptionRule);

        // Demo3 inherits the name rule from Demo2 (nearest) and the description rule from Demo1
        Assert.Equal("${D} ${A}", modelGraph.Types["sample1/Demo3"].DisplayNameRule);
        Assert.Equal("${B} (${C})", modelGraph.Types["sample1/Demo3"].DisplayDescriptionRule);
    }

    [Fact]
    public void DisplayRule_ReferencingInheritedAttribute_OK()
    {
        var sampleModel = sampleData.sample1.Builder.Build();
        // Demo2 references attribute A, which is inherited from Demo1
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo2").DisplayNameRule = "${A ?? D}";

        OperationResult operationResult = new();
        Resolve(sampleModel, operationResult);

        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void DisplayRule_RecordPath_OK()
    {
        var sampleModel = sampleData.sample1.Builder.Build();
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo1").Attributes!.Add(
            new() { CkAttributeId = "sample1/Record1", AttributeName = "Rec" });
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo1").DisplayNameRule = "${Rec.A}";

        OperationResult operationResult = new();
        Resolve(sampleModel, operationResult);

        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void DisplayRule_UnknownRecordField_CompilerErrorMessage()
    {
        var sampleModel = sampleData.sample1.Builder.Build();
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo1").Attributes!.Add(
            new() { CkAttributeId = "sample1/Record1", AttributeName = "Rec" });
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo1").DisplayNameRule = "${Rec.X}";

        OperationResult operationResult = new();
        Resolve(sampleModel, operationResult);

        var message = Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, message.MessageLevel);
        Assert.Equal(68, message.MessageNumber);
        Assert.Contains("Rec.X", message.MessageText);
    }

    [Fact]
    public void DisplayRule_PathThroughNonRecordAttribute_CompilerErrorMessage()
    {
        var sampleModel = sampleData.sample1.Builder.Build();
        // A is a plain string attribute — it has no record fields to traverse into
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo1").DisplayNameRule = "${A.field}";

        OperationResult operationResult = new();
        Resolve(sampleModel, operationResult);

        var message = Assert.Single(operationResult.Messages);
        Assert.Equal(68, message.MessageNumber);
    }

    [Fact]
    public void DisplayRule_UnknownAttribute_CompilerErrorMessage()
    {
        var sampleModel = sampleData.sample1.Builder.Build();
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo1").DisplayNameRule = "${doesNotExist}";

        OperationResult operationResult = new();
        Resolve(sampleModel, operationResult);

        var message = Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, message.MessageLevel);
        Assert.Equal(68, message.MessageNumber);
        Assert.Contains("doesNotExist", message.MessageText);
    }

    [Fact]
    public void DisplayRule_InvalidSyntax_CompilerErrorMessage()
    {
        var sampleModel = sampleData.sample1.Builder.Build();
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo1").DisplayDescriptionRule = "${A";

        OperationResult operationResult = new();
        Resolve(sampleModel, operationResult);

        var message = Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, message.MessageLevel);
        Assert.Equal(67, message.MessageNumber);
        Assert.Contains("displayDescriptionRule", message.MessageText);
    }

    [Fact]
    public void DisplayRule_InvalidRuleOnBaseType_ReportedOnlyAtDeclaringType()
    {
        var sampleModel = sampleData.sample1.Builder.Build();
        // Invalid rule on Demo1 is inherited by Demo2/Demo3 — the error must be reported once
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo1").DisplayNameRule = "${doesNotExist}";

        OperationResult operationResult = new();
        Resolve(sampleModel, operationResult);

        var message = Assert.Single(operationResult.Messages);
        Assert.Contains("Demo1", message.MessageText);
    }
}
