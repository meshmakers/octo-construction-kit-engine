using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;
using Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.systemFake;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Resolvers;

public class ReferenceResolverTests
{
    [Fact]
    public void Resolve_OK()
    {
        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(Builder.Build());
        modelGraph.AppendModel(sampleData.sample1.Builder.Build());

        OperationResult operationResult = new();
        OriginFileResolver originFileResolver = new("TEST");
        ReferenceResolver modelResolver = new();
        modelResolver.Resolve(modelGraph, originFileResolver, operationResult);

        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void Resolve_Records_OK()
    {
        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(Builder.Build());
        modelGraph.AppendModel(sampleData.records1.Builder.Build());

        OperationResult operationResult = new();
        OriginFileResolver originFileResolver = new("TEST");
        ReferenceResolver modelResolver = new();
        modelResolver.Resolve(modelGraph, originFileResolver, operationResult);

        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void Resolve_Attribute_CkRecordIdDoesNotExist_CompilerErrorMessage()
    {
        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(Builder.Build());
        modelGraph.AppendModel(sampleData.records1_recordIdDoesNotExist.Builder.Build());

        OperationResult operationResult = new();
        OriginFileResolver originFileResolver = new("TEST");
        ReferenceResolver modelResolver = new();
        modelResolver.Resolve(modelGraph, originFileResolver, operationResult);

        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[0].MessageLevel);
        Assert.Equal(41, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public void Resolve_Record_AttributeDoesNotExist_CompilerErrorMessage()
    {
        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(Builder.Build());
        modelGraph.AppendModel(sampleData.records1_attributeIdDoesNotExist.Builder.Build());

        OperationResult operationResult = new();
        OriginFileResolver originFileResolver = new("TEST");
        ReferenceResolver modelResolver = new();
        modelResolver.Resolve(modelGraph, originFileResolver, operationResult);

        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[0].MessageLevel);
        Assert.Equal(42, operationResult.Messages[0].MessageNumber);
    }


    [Fact]
    public void Resolve_Record_DerivedDoesNotExist_CompilerErrorMessage()
    {
        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(Builder.Build());
        modelGraph.AppendModel(sampleData.records1_derivedFromDoesNotExist.Builder.Build());

        OperationResult operationResult = new();
        OriginFileResolver originFileResolver = new("TEST");
        ReferenceResolver modelResolver = new();
        modelResolver.Resolve(modelGraph, originFileResolver, operationResult);

        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[0].MessageLevel);
        Assert.Equal(43, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public void Resolve_Type_DerivedDoesNotExist_CompilerErrorMessage()
    {
        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(Builder.Build());
        modelGraph.AppendModel(sampleData.sample_types_unknownTypeId.Builder.Build());

        OperationResult operationResult = new();
        OriginFileResolver originFileResolver = new("TEST");
        ReferenceResolver modelResolver = new();
        modelResolver.Resolve(modelGraph, originFileResolver, operationResult);

        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[0].MessageLevel);
        Assert.Equal(3, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public void Resolve_Type_AttributeDoesNotExist_CompilerErrorMessage()
    {
        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(Builder.Build());
        modelGraph.AppendModel(sampleData.sample_types_unkownAttributeId.Builder.Build());

        OperationResult operationResult = new();
        OriginFileResolver originFileResolver = new("TEST");
        ReferenceResolver modelResolver = new();
        modelResolver.Resolve(modelGraph, originFileResolver, operationResult);

        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[0].MessageLevel);
        Assert.Equal(2, operationResult.Messages[0].MessageNumber);
    }
}