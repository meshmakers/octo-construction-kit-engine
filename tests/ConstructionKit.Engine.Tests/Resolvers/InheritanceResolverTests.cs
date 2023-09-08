using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;
using Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.sample_TypeNotDerivedFromSystemEntity_fail;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Resolvers;

public class InheritanceResolverTests
{
    [Fact]
    public void Inheritance_InheritanceOfAssociations_OK()
    {
        var logger = A.Fake<ILogger<InheritanceResolver> >();

        CkAggregatedModelElements ckAggregatedModelElements = new();
        ckAggregatedModelElements.AppendModel(sampleData.systemFake.Builder.Build());
        ckAggregatedModelElements.AppendModel(sampleData.sample1.Builder.Build());

        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        CkModelGraph graph = new();
        inheritanceResolver.Resolve(ckAggregatedModelElements, graph, operationResult);
        
        Assert.Empty(operationResult.Messages);
        Assert.Equal(4, graph.Types.Count);
        Assert.NotNull(graph.Types["System/Entity"]);
        Assert.NotNull(graph.Types["sample1/Demo1"]);
        Assert.NotNull(graph.Types["sample1/Demo2"]);
        Assert.Equal(0, graph.Types["System/Entity"].Attributes.Count);
        Assert.Equal(1, graph.Types["System/Entity"].Associations.In.Owned.Count);
        Assert.Contains(graph.Types["System/Entity"].Associations.In.Owned, a=> a.CkRoleId == "sample1/Related");
        Assert.Equal(0, graph.Types["System/Entity"].Associations.In.Inherited.Count);
        Assert.Equal(0, graph.Types["System/Entity"].Associations.Out.Owned.Count);
        Assert.Equal(0, graph.Types["System/Entity"].Associations.Out.Inherited.Count);
        
        Assert.Equal(3, graph.Types["sample1/Demo1"].Attributes.Count);
        Assert.Equal(1, graph.Types["sample1/Demo1"].Associations.In.Owned.Count);
        Assert.Contains(graph.Types["sample1/Demo1"].Associations.In.Owned, a=> a.CkRoleId == "System/ParentChild");
        Assert.Equal(1, graph.Types["sample1/Demo1"].Associations.In.Inherited.Count);
        Assert.Contains(graph.Types["sample1/Demo1"].Associations.In.Inherited, a=> a.CkRoleId == "sample1/Related");
        Assert.Equal(0, graph.Types["sample1/Demo1"].Associations.Out.Inherited.Count);
        Assert.Equal(0, graph.Types["sample1/Demo1"].Associations.Out.Owned.Count);
        
        Assert.Equal(6, graph.Types["sample1/Demo2"].Attributes.Count);
        Assert.Equal(0, graph.Types["sample1/Demo2"].Associations.In.Owned.Count);
        Assert.Equal(2, graph.Types["sample1/Demo2"].Associations.In.Inherited.Count);
        Assert.Contains(graph.Types["sample1/Demo2"].Associations.In.Inherited, a=> a.CkRoleId == "sample1/Related");
        Assert.Contains(graph.Types["sample1/Demo2"].Associations.In.Inherited, a=> a.CkRoleId == "System/ParentChild");
        Assert.Equal(0, graph.Types["sample1/Demo2"].Associations.Out.Inherited.Count);
        Assert.Equal(1, graph.Types["sample1/Demo2"].Associations.Out.Owned.Count);
        Assert.Contains(graph.Types["sample1/Demo2"].Associations.Out.Owned, a=> a.CkRoleId == "System/ParentChild");
    }

    [Fact]
    public void Inheritance_InheritanceOfAttributes_OK()
    {
        var logger = A.Fake<ILogger<InheritanceResolver>>();

        CkAggregatedModelElements ckAggregatedModelElements = new();
        ckAggregatedModelElements.AppendModel(sampleData.systemFake.Builder.Build());
        ckAggregatedModelElements.AppendModel(sampleData.sample1.Builder.Build());
        
        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        CkModelGraph graph = new();
        inheritanceResolver.Resolve(ckAggregatedModelElements, graph, operationResult);
        
        Assert.Empty(operationResult.Messages);
        Assert.Equal(4, graph.Types.Count);
        Assert.NotNull(graph.Types["System/Entity"]);
        Assert.NotNull(graph.Types["sample1/Demo1"]);
        Assert.NotNull(graph.Types["sample1/Demo2"]);
        Assert.Equal(0, graph.Types["System/Entity"].Attributes.Count);
        Assert.Equal(3, graph.Types["sample1/Demo1"].Attributes.Count);
        Assert.Equal(6, graph.Types["sample1/Demo2"].Attributes.Count);
    }
    
    [Fact]
    public void Inheritance_AttributesSameNameOnLevel_CompilerErrorMessage_ThrowsException()
    {
        var logger = A.Fake<ILogger<InheritanceResolver>>();

        CkAggregatedModelElements ckAggregatedModelElements = new();
        ckAggregatedModelElements.AppendModel(sampleData.systemFake.Builder.Build());
        ckAggregatedModelElements.AppendModel(sampleData.sample_attributes_sameName_fail.Builder.Build());
        
        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        CkModelGraph graph = new();
        Assert.Throws<ModelValidationException>(() => inheritanceResolver.Resolve(ckAggregatedModelElements, graph, operationResult));

        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.FatalError, operationResult.Messages[0].MessageLevel);
        Assert.Equal(15, operationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void Inheritance_AttributesSameNameOnInheritance_CompilerErrorMessage()
    {
        var logger = A.Fake<ILogger<InheritanceResolver>>();

        CkAggregatedModelElements ckAggregatedModelElements = new();
        ckAggregatedModelElements.AppendModel(sampleData.systemFake.Builder.Build());
        ckAggregatedModelElements.AppendModel(sampleData.sample_attributes_sameNameAtInheritance_fail.Builder.Build());
        
        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        CkModelGraph graph = new();
        inheritanceResolver.Resolve(ckAggregatedModelElements, graph, operationResult);
        
        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[0].MessageLevel);
        Assert.Equal(13, operationResult.Messages[0].MessageNumber);
    }
        
    [Fact]
    public void Inheritance_AttributesSameIdOnLevel_CompilerErrorMessage_ThrowsException()
    {
        var logger = A.Fake<ILogger<InheritanceResolver>>();

        CkAggregatedModelElements ckAggregatedModelElements = new();
        ckAggregatedModelElements.AppendModel(sampleData.systemFake.Builder.Build());
        ckAggregatedModelElements.AppendModel(sampleData.sample_attributes_sameId_fail.Builder.Build());
        
        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        CkModelGraph graph = new();
        Assert.Throws<ModelValidationException>(() => inheritanceResolver.Resolve(ckAggregatedModelElements, graph, operationResult));

        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.FatalError, operationResult.Messages[0].MessageLevel);
        Assert.Equal(16, operationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void Inheritance_AttributesSameIdOnInheritance_CompilerErrorMessage()
    {
        var logger = A.Fake<ILogger<InheritanceResolver>>();

        CkAggregatedModelElements ckAggregatedModelElements = new();
        ckAggregatedModelElements.AppendModel(sampleData.systemFake.Builder.Build());
        ckAggregatedModelElements.AppendModel(sampleData.sample_attributes_sameIdAtInheritance_fail.Builder.Build());
        
        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        CkModelGraph graph = new();
        inheritanceResolver.Resolve(ckAggregatedModelElements, graph, operationResult);

        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[0].MessageLevel);
        Assert.Equal(12, operationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void Inheritance_AssociationSameIdAndTargetOnSame_CompilerErrorMessage()
    {
        var logger = A.Fake<ILogger<InheritanceResolver>>();

        CkAggregatedModelElements ckAggregatedModelElements = new();
        ckAggregatedModelElements.AppendModel(sampleData.systemFake.Builder.Build());
        ckAggregatedModelElements.AppendModel(sampleData.sample_assocs_sameIdAndTarget_fail.Builder.Build());
        
        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        CkModelGraph graph = new();
        inheritanceResolver.Resolve(ckAggregatedModelElements, graph, operationResult);

        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[0].MessageLevel);
        Assert.Equal(14, operationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void Inheritance_AssociationSameIdAndTargetOnInheritance_CompilerErrorMessage()
    {
        var logger = A.Fake<ILogger<InheritanceResolver>>();

        CkAggregatedModelElements ckAggregatedModelElements = new();
        ckAggregatedModelElements.AppendModel(sampleData.systemFake.Builder.Build());
        ckAggregatedModelElements.AppendModel(sampleData.sample_assocs_sameIdAndTargetAtInheritance_fail.Builder.Build());
        
        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        CkModelGraph graph = new();
        inheritanceResolver.Resolve(ckAggregatedModelElements, graph, operationResult);

        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[0].MessageLevel);
        Assert.Equal(20, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public void Inheritance_AssociationSameIdAndBaseTargetOnInheritance_CompilerErrorMessage()
    {
        var logger = A.Fake<ILogger<InheritanceResolver>>();

        CkAggregatedModelElements ckAggregatedModelElements = new();
        ckAggregatedModelElements.AppendModel(sampleData.systemFake.Builder.Build());
        ckAggregatedModelElements.AppendModel(sampleData.sample_assocs_sameIdAndBaseTargetAtInheritance_fail.Builder.Build());
        
        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        CkModelGraph graph = new();
        inheritanceResolver.Resolve(ckAggregatedModelElements, graph, operationResult);

        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[0].MessageLevel);
        Assert.Equal(20, operationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void Inheritance_AssociationSameRoleIdDifferentTrees()
    {
        var logger = A.Fake<ILogger<InheritanceResolver>>();

        CkAggregatedModelElements ckAggregatedModelElements = new();
        ckAggregatedModelElements.AppendModel(sampleData.systemFake.Builder.Build());
        ckAggregatedModelElements.AppendModel(sampleData.sample_assocs_sameRoleIdDifferentTrees_ok.Builder.Build());
        
        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        CkModelGraph graph = new();
        inheritanceResolver.Resolve(ckAggregatedModelElements, graph, operationResult);

        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void MissingInheritanceType_CompilerErrorMessage_ThrowsException()
    {
        var logger = A.Fake<ILogger<InheritanceResolver> >();

        CkAggregatedModelElements ckAggregatedModelElements = new();
        ckAggregatedModelElements.AppendModel(sampleData.sample1.Builder.Build());

        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        CkModelGraph graph = new();
        Assert.Throws<ModelValidationException>(() => inheritanceResolver.Resolve(ckAggregatedModelElements, graph, operationResult));
        
        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.FatalError, operationResult.Messages[0].MessageLevel);
        Assert.Equal(11, operationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void AssociationTargetUnknown_CompilerErrorMessage_ThrowsException()
    {
        var logger = A.Fake<ILogger<InheritanceResolver> >();

        CkAggregatedModelElements ckAggregatedModelElements = new();
        ckAggregatedModelElements.AppendModel(sampleData.systemFake.Builder.Build());
        ckAggregatedModelElements.AppendModel(sampleData.sample_assocs_invalidTarget_fail.Builder.Build());

        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        CkModelGraph graph = new();
        Assert.Throws<ModelValidationException>(() => inheritanceResolver.Resolve(ckAggregatedModelElements, graph, operationResult));
        
        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.FatalError, operationResult.Messages[0].MessageLevel);
        Assert.Equal(18, operationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void DerivedFromFinal_CompilerErrorMessage_ThrowsException()
    {
        var logger = A.Fake<ILogger<InheritanceResolver> >();

        CkAggregatedModelElements ckAggregatedModelElements = new();
        ckAggregatedModelElements.AppendModel(sampleData.systemFake.Builder.Build());
        ckAggregatedModelElements.AppendModel(sampleData.sample_final_fail.Builder.Build());

        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        CkModelGraph graph = new();
        Assert.Throws<ModelValidationException>(() => inheritanceResolver.Resolve(ckAggregatedModelElements, graph, operationResult));
        
        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.FatalError, operationResult.Messages[0].MessageLevel);
        Assert.Equal(21, operationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void DerivedTypeDefinesFinal_OK()
    {
        var logger = A.Fake<ILogger<InheritanceResolver> >();

        CkAggregatedModelElements ckAggregatedModelElements = new();
        ckAggregatedModelElements.AppendModel(sampleData.systemFake.Builder.Build());
        ckAggregatedModelElements.AppendModel(sampleData.sample_final.Builder.Build());

        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        CkModelGraph graph = new();
        inheritanceResolver.Resolve(ckAggregatedModelElements, graph, operationResult);
        
        Assert.Empty(operationResult.Messages);
    }
    
    [Fact]
    public void TypeNotDerivedFromSystemEntity_CompilerErrorMessage_ThrowsException()
    {
        var logger = A.Fake<ILogger<InheritanceResolver> >();

        CkAggregatedModelElements ckAggregatedModelElements = new();
        ckAggregatedModelElements.AppendModel(sampleData.systemFake.Builder.Build());
        ckAggregatedModelElements.AppendModel(Builder.Build());

        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        CkModelGraph graph = new();
        Assert.Throws<ModelValidationException>(() => inheritanceResolver.Resolve(ckAggregatedModelElements, graph, operationResult));

        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.FatalError, operationResult.Messages[0].MessageLevel);
        Assert.Equal(9, operationResult.Messages[0].MessageNumber);
    }
    

}