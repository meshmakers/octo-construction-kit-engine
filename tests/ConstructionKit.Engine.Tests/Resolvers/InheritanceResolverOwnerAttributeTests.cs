using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;
using Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.systemFake;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Resolvers;

/// <summary>
///     Owner attribute path inheritance for owned-only data permissions (AB#4978): nearest declared
///     path wins along the derivedFromCkTypeId chain, and compile-time validation walks the path —
///     Record segments are traversable, RecordArray segments are rejected, the terminal segment must
///     be String. Message number: 69 = OwnerAttributeInvalid.
/// </summary>
public class InheritanceResolverOwnerAttributeTests
{
    private readonly ILoggerFactory _loggerFactory;

    public InheritanceResolverOwnerAttributeTests(ITestOutputHelper output)
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
    public void OwnerAttribute_InheritedByDerivedTypes_NearestDeclarationWins()
    {
        var sampleModel = sampleData.sample1.Builder.Build();
        // Demo1 declares A; Demo2 overrides with its own D; Demo3 declares nothing.
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo1").OwnerAttributePath = "A";
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo2").OwnerAttributePath = "D";

        OperationResult operationResult = new();
        var modelGraph = Resolve(sampleModel, operationResult);

        Assert.Empty(operationResult.Messages);

        Assert.Equal("A", modelGraph.Types["sample1/Demo1"].OwnerAttributePath);
        Assert.Equal("D", modelGraph.Types["sample1/Demo2"].OwnerAttributePath);
        // Demo3 inherits from the nearest declaring base (Demo2)
        Assert.Equal("D", modelGraph.Types["sample1/Demo3"].OwnerAttributePath);
    }

    [Fact]
    public void OwnerAttribute_UndeclaredEverywhere_StaysNull()
    {
        var sampleModel = sampleData.sample1.Builder.Build();

        OperationResult operationResult = new();
        var modelGraph = Resolve(sampleModel, operationResult);

        Assert.Empty(operationResult.Messages);
        Assert.Null(modelGraph.Types["sample1/Demo1"].OwnerAttributePath);
        Assert.Null(modelGraph.Types["sample1/Demo3"].OwnerAttributePath);
    }

    [Fact]
    public void OwnerAttribute_ReferencingInheritedAttribute_OK()
    {
        var sampleModel = sampleData.sample1.Builder.Build();
        // Demo2 references attribute A, which is inherited from Demo1
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo2").OwnerAttributePath = "A";

        OperationResult operationResult = new();
        Resolve(sampleModel, operationResult);

        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void OwnerAttribute_UnknownAttribute_CompilerErrorMessage()
    {
        var sampleModel = sampleData.sample1.Builder.Build();
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo1").OwnerAttributePath = "doesNotExist";

        OperationResult operationResult = new();
        Resolve(sampleModel, operationResult);

        var message = Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, message.MessageLevel);
        Assert.Equal(69, message.MessageNumber);
        Assert.Contains("doesNotExist", message.MessageText);
    }

    [Fact]
    public void OwnerAttribute_RecordPath_OK()
    {
        var sampleModel = sampleData.sample1.Builder.Build();
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo1").Attributes!.Add(
            new() { CkAttributeId = "sample1/Record1", AttributeName = "Rec" });
        // Record segments are traversable; Record1.A is a String field.
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo1").OwnerAttributePath = "Rec.A";

        OperationResult operationResult = new();
        var modelGraph = Resolve(sampleModel, operationResult);

        Assert.Empty(operationResult.Messages);
        Assert.Equal("Rec.A", modelGraph.Types["sample1/Demo3"].OwnerAttributePath);
    }

    [Fact]
    public void OwnerAttribute_UnknownRecordField_CompilerErrorMessage()
    {
        var sampleModel = sampleData.sample1.Builder.Build();
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo1").Attributes!.Add(
            new() { CkAttributeId = "sample1/Record1", AttributeName = "Rec" });
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo1").OwnerAttributePath = "Rec.X";

        OperationResult operationResult = new();
        Resolve(sampleModel, operationResult);

        var message = Assert.Single(operationResult.Messages);
        Assert.Equal(69, message.MessageNumber);
        Assert.Contains("'X'", message.MessageText);
    }

    [Fact]
    public void OwnerAttribute_RecordArraySegment_CompilerErrorMessage()
    {
        var sampleModel = sampleData.sample1.Builder.Build();
        sampleModel.Attributes!.Add(new()
        {
            AttributeId = "RecArr",
            ValueType = AttributeValueTypesDto.RecordArray,
            ValueCkRecordId = "sample1/Record1"
        });
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo1").Attributes!.Add(
            new() { CkAttributeId = "sample1/RecArr", AttributeName = "Items" });
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo1").OwnerAttributePath = "Items.A";

        OperationResult operationResult = new();
        Resolve(sampleModel, operationResult);

        var message = Assert.Single(operationResult.Messages);
        Assert.Equal(69, message.MessageNumber);
        Assert.Contains("RecordArray", message.MessageText);
    }

    [Fact]
    public void OwnerAttribute_PathThroughNonRecordAttribute_CompilerErrorMessage()
    {
        var sampleModel = sampleData.sample1.Builder.Build();
        // A is a plain String attribute — it has no record fields to traverse into.
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo1").OwnerAttributePath = "A.Field";

        OperationResult operationResult = new();
        Resolve(sampleModel, operationResult);

        var message = Assert.Single(operationResult.Messages);
        Assert.Equal(69, message.MessageNumber);
        Assert.Contains("cannot be traversed", message.MessageText);
    }

    [Fact]
    public void OwnerAttribute_NonStringAttribute_CompilerErrorMessage()
    {
        var sampleModel = sampleData.sample1.Builder.Build();
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo1").Attributes!.Add(
            new() { CkAttributeId = "sample1/Record1", AttributeName = "Rec" });
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo1").OwnerAttributePath = "Rec";

        OperationResult operationResult = new();
        Resolve(sampleModel, operationResult);

        var message = Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, message.MessageLevel);
        Assert.Equal(69, message.MessageNumber);
        Assert.Contains("String", message.MessageText);
    }

    [Fact]
    public void OwnerAttribute_InvalidOnBaseType_ReportedOnlyAtDeclaringType()
    {
        var sampleModel = sampleData.sample1.Builder.Build();
        // Invalid name on Demo1 is inherited by Demo2/Demo3 — the error must be reported once
        sampleModel.Types!.Single(t => t.TypeId.Name == "Demo1").OwnerAttributePath = "doesNotExist";

        OperationResult operationResult = new();
        Resolve(sampleModel, operationResult);

        var message = Assert.Single(operationResult.Messages);
        Assert.Contains("Demo1", message.MessageText);
    }
}
