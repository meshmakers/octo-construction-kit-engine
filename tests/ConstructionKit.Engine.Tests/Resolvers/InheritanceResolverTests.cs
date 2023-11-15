using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Resolvers;

public class InheritanceResolverTests
{
    private readonly ILoggerFactory _loggerFactory;

    public InheritanceResolverTests(ITestOutputHelper output)
    {
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddXUnit(output); // Redirect logs to xUnit test output
        });
    }

    
    [Fact]
    public void Inheritance_InheritanceOfAssociations_OK()
    {
        var logger = _loggerFactory.CreateLogger<InheritanceResolver>();

        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(sampleData.systemFake.Builder.Build());
        modelGraph.AppendModel(sampleData.sample1.Builder.Build());

        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        inheritanceResolver.Resolve(modelGraph, operationResult);
        
        Assert.Empty(operationResult.Messages);
        Assert.Equal(4, modelGraph.Types.Count);
        Assert.NotNull(modelGraph.Types["System/Entity"]);
        Assert.NotNull(modelGraph.Types["sample1/Demo1"]);
        Assert.NotNull(modelGraph.Types["sample1/Demo2"]);
        Assert.Empty(modelGraph.Types["System/Entity"].AllAttributes);
        Assert.Single(modelGraph.Types["System/Entity"].Associations.In.Owned);
        Assert.Contains(modelGraph.Types["System/Entity"].Associations.In.Owned, a=> a.CkRoleId == "sample1/Related");
        Assert.Empty(modelGraph.Types["System/Entity"].Associations.In.Inherited);
        Assert.Empty(modelGraph.Types["System/Entity"].Associations.Out.Owned);
        Assert.Empty(modelGraph.Types["System/Entity"].Associations.Out.Inherited);
        
        Assert.Equal(3, modelGraph.Types["sample1/Demo1"].AllAttributes.Count);
        Assert.Single(modelGraph.Types["sample1/Demo1"].Associations.In.Owned);
        Assert.Contains(modelGraph.Types["sample1/Demo1"].Associations.In.Owned, a=> a.CkRoleId == "System/ParentChild");
        Assert.Single(modelGraph.Types["sample1/Demo1"].Associations.In.Inherited);
        Assert.Contains(modelGraph.Types["sample1/Demo1"].Associations.In.Inherited, a=> a.CkRoleId == "sample1/Related");
        Assert.Empty(modelGraph.Types["sample1/Demo1"].Associations.Out.Inherited);
        Assert.Empty(modelGraph.Types["sample1/Demo1"].Associations.Out.Owned);
        
        Assert.Equal(7, modelGraph.Types["sample1/Demo2"].AllAttributes.Count);
        Assert.Empty(modelGraph.Types["sample1/Demo2"].Associations.In.Owned);
        Assert.Equal(2, modelGraph.Types["sample1/Demo2"].Associations.In.Inherited.Count);
        Assert.Contains(modelGraph.Types["sample1/Demo2"].Associations.In.Inherited, a=> a.CkRoleId == "sample1/Related");
        Assert.Contains(modelGraph.Types["sample1/Demo2"].Associations.In.Inherited, a=> a.CkRoleId == "System/ParentChild");
        Assert.Empty(modelGraph.Types["sample1/Demo2"].Associations.Out.Inherited);
        Assert.Single(modelGraph.Types["sample1/Demo2"].Associations.Out.Owned);
        Assert.Contains(modelGraph.Types["sample1/Demo2"].Associations.Out.Owned, a=> a.CkRoleId == "System/ParentChild");
    }

    [Fact]
    public void Inheritance_InheritanceOfAttributes_OK()
    {
        var logger = _loggerFactory.CreateLogger<InheritanceResolver>();

        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(sampleData.systemFake.Builder.Build());
        modelGraph.AppendModel(sampleData.sample1.Builder.Build());
        
        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        inheritanceResolver.Resolve(modelGraph, operationResult);
        
        Assert.Empty(operationResult.Messages);
        Assert.Equal(4, modelGraph.Types.Count);
        Assert.NotNull(modelGraph.Types["System/Entity"]);
        Assert.NotNull(modelGraph.Types["sample1/Demo1"]);
        Assert.NotNull(modelGraph.Types["sample1/Demo2"]);
        Assert.Empty(modelGraph.Types["System/Entity"].AllAttributes);
        Assert.Equal(3, modelGraph.Types["sample1/Demo1"].AllAttributes.Count);
        Assert.Equal(7, modelGraph.Types["sample1/Demo2"].AllAttributes.Count);
    }

    [Fact]
    public void Inheritance_DerivedTypes_OK()
    {
        var logger = _loggerFactory.CreateLogger<InheritanceResolver>();

        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(sampleData.systemFake.Builder.Build());
        modelGraph.AppendModel(sampleData.sample1.Builder.Build());
        
        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        inheritanceResolver.Resolve(modelGraph, operationResult);
        
        Assert.Empty(operationResult.Messages);
        Assert.Equal(4, modelGraph.Types.Count);
        Assert.NotNull(modelGraph.Types["System/Entity"]);
        Assert.NotNull(modelGraph.Types["sample1/Demo1"]);
        Assert.NotNull(modelGraph.Types["sample1/Demo2"]);
        Assert.NotNull(modelGraph.Types["sample1/Demo3"]);
        
        Assert.Equal(3, modelGraph.Types["System/Entity"].DerivedTypes.Count);
        Assert.Contains(modelGraph.Types["System/Entity"].DerivedTypes, x => x.InheritorCkTypeId == "sample1/Demo1");
        Assert.Contains(modelGraph.Types["System/Entity"].DerivedTypes, x => x.InheritorCkTypeId == "sample1/Demo2");
        Assert.Contains(modelGraph.Types["System/Entity"].DerivedTypes, x => x.InheritorCkTypeId == "sample1/Demo3");
        
        Assert.Equal(2, modelGraph.Types["sample1/Demo1"].DerivedTypes.Count);
        Assert.Contains(modelGraph.Types["sample1/Demo1"].DerivedTypes, x => x.InheritorCkTypeId == "sample1/Demo2");
        Assert.Contains(modelGraph.Types["sample1/Demo1"].DerivedTypes, x => x.InheritorCkTypeId == "sample1/Demo3");
        
        Assert.Single(modelGraph.Types["sample1/Demo2"].DerivedTypes);
        Assert.Contains(modelGraph.Types["sample1/Demo2"].DerivedTypes, x => x.InheritorCkTypeId == "sample1/Demo3");

        Assert.Empty(modelGraph.Types["sample1/Demo3"].DerivedTypes);
    }
    
    [Fact]
    public void GetAllDerivedTypes_OK()
    {
        var logger = _loggerFactory.CreateLogger<InheritanceResolver>();

        CkModelGraph ckModelGraph = new();
        ckModelGraph.AppendModel(sampleData.systemFake.Builder.Build());
        ckModelGraph.AppendModel(sampleData.sample1.Builder.Build());
        
        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        inheritanceResolver.Resolve(ckModelGraph,  operationResult);
        
        Assert.Empty(operationResult.Messages);

        var systemEntity = ckModelGraph.Types["System/Entity"];
        var r = systemEntity.GetAllDerivedTypes(true);
        
        Assert.Equal(4, r.Count);
        Assert.Contains(r ,x => x == "System/Entity");
        Assert.Contains(r ,x => x == "sample1/Demo1");
        Assert.Contains(r, x => x == "sample1/Demo2");
        Assert.Contains(r, x => x == "sample1/Demo3");
    }
    
    [Fact]
    public void GetBaseTypes_OK()
    {
        var logger = _loggerFactory.CreateLogger<InheritanceResolver>();

        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(sampleData.systemFake.Builder.Build());
        modelGraph.AppendModel(sampleData.sample1.Builder.Build());
        
        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        inheritanceResolver.Resolve(modelGraph, operationResult);
        
        Assert.Empty(operationResult.Messages);

        var demo3 = modelGraph.Types["sample1/Demo3"];
        var r = demo3.GetBaseTypes(true);
        
        Assert.Equal(4, r.Count);
        Assert.Contains(r ,x => x == "System/Entity");
        Assert.Contains(r ,x => x == "sample1/Demo1");
        Assert.Contains(r, x => x == "sample1/Demo2");
        Assert.Contains(r, x => x == "sample1/Demo3");
    }
    
    [Fact]
    public void Inheritance_AttributesSameNameOnInheritance_CompilerErrorMessage()
    {
        var logger = _loggerFactory.CreateLogger<InheritanceResolver>();

        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(sampleData.systemFake.Builder.Build());
        modelGraph.AppendModel(sampleData.sample_attributes_sameNameAtInheritance_fail.Builder.Build());
        
        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        inheritanceResolver.Resolve(modelGraph, operationResult);
        
        Assert.Equal(2, operationResult.Messages.Count);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[0].MessageLevel);
        Assert.Contains("sample1/Demo2", operationResult.Messages[0].MessageText);
        Assert.Equal(13, operationResult.Messages[0].MessageNumber);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[1].MessageLevel);
        Assert.Equal(13, operationResult.Messages[1].MessageNumber);
        Assert.Contains("sample1/Demo3", operationResult.Messages[1].MessageText);
    }
    
    [Fact]
    public void Inheritance_AttributesSameIdOnInheritance_CompilerErrorMessage()
    {
        var logger = _loggerFactory.CreateLogger<InheritanceResolver>();

        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(sampleData.systemFake.Builder.Build());
        modelGraph.AppendModel(sampleData.sample_attributes_sameIdAtInheritance_fail.Builder.Build());
        
        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        inheritanceResolver.Resolve(modelGraph, operationResult);

        Assert.Equal(2, operationResult.Messages.Count);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[0].MessageLevel);
        Assert.Contains("sample1/Demo1", operationResult.Messages[0].MessageText);
        Assert.Contains("sample1/Demo2", operationResult.Messages[0].MessageText);
        Assert.Equal(12, operationResult.Messages[0].MessageNumber);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[1].MessageLevel);
        Assert.Equal(12, operationResult.Messages[1].MessageNumber);
        Assert.Contains("sample1/Demo2", operationResult.Messages[1].MessageText);
        Assert.Contains("sample1/Demo3", operationResult.Messages[1].MessageText);
    }
    
    [Fact]
    public void Inheritance_AssociationSameIdAndTargetOnSame_CompilerErrorMessage()
    {
        var logger = _loggerFactory.CreateLogger<InheritanceResolver>();

        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(sampleData.systemFake.Builder.Build());
        modelGraph.AppendModel(sampleData.sample_assocs_sameIdAndTarget_fail.Builder.Build());
        
        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        inheritanceResolver.Resolve(modelGraph, operationResult);

        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[0].MessageLevel);
        Assert.Equal(14, operationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void Inheritance_AssociationUnknownRoleId_CompilerErrorMessage()
    {
        var logger = _loggerFactory.CreateLogger<InheritanceResolver>();

        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(sampleData.systemFake.Builder.Build());
        modelGraph.AppendModel(sampleData.sample_assocs_unknownRoleId_fail.Builder.Build());
        
        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        inheritanceResolver.Resolve(modelGraph, operationResult);

        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[0].MessageLevel);
        Assert.Equal(48, operationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void Inheritance_AssociationSameIdAndTargetOnInheritance_CompilerErrorMessage()
    {
        var logger = _loggerFactory.CreateLogger<InheritanceResolver>();

        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(sampleData.systemFake.Builder.Build());
        modelGraph.AppendModel(sampleData.sample_assocs_sameIdAndTargetAtInheritance_fail.Builder.Build());
        
        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        inheritanceResolver.Resolve(modelGraph, operationResult);

        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[0].MessageLevel);
        Assert.Equal(20, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public void Inheritance_AssociationSameIdAndBaseTargetOnInheritance_CompilerErrorMessage()
    {
        var logger = _loggerFactory.CreateLogger<InheritanceResolver>();

        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(sampleData.systemFake.Builder.Build());
        modelGraph.AppendModel(sampleData.sample_assocs_sameIdAndBaseTargetAtInheritance_fail.Builder.Build());
        
        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        inheritanceResolver.Resolve(modelGraph, operationResult);

        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[0].MessageLevel);
        Assert.Equal(20, operationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void Inheritance_AssociationSameRoleIdDifferentTrees()
    {
        var logger = _loggerFactory.CreateLogger<InheritanceResolver>();

        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(sampleData.systemFake.Builder.Build());
        modelGraph.AppendModel(sampleData.sample_assocs_sameRoleIdDifferentTrees_ok.Builder.Build());
        
        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        inheritanceResolver.Resolve(modelGraph, operationResult);

        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void MissingInheritanceType_CompilerErrorMessage_ThrowsException()
    {
        var logger = _loggerFactory.CreateLogger<InheritanceResolver>();

        CkModelGraph ckAggregatedModelElements = new();
        ckAggregatedModelElements.AppendModel(sampleData.sample1.Builder.Build());

        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        Assert.Throws<ModelValidationException>(() => inheritanceResolver.Resolve(ckAggregatedModelElements, operationResult));
        
        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.FatalError, operationResult.Messages[0].MessageLevel);
        Assert.Equal(11, operationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void AssociationTargetUnknown_CompilerErrorMessage_ThrowsException()
    {
        var logger = _loggerFactory.CreateLogger<InheritanceResolver>();

        CkModelGraph ckAggregatedModelElements = new();
        ckAggregatedModelElements.AppendModel(sampleData.systemFake.Builder.Build());
        ckAggregatedModelElements.AppendModel(sampleData.sample_assocs_invalidTarget_fail.Builder.Build());

        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        Assert.Throws<ModelValidationException>(() => inheritanceResolver.Resolve(ckAggregatedModelElements, operationResult));
        
        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.FatalError, operationResult.Messages[0].MessageLevel);
        Assert.Equal(18, operationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void DerivedFromFinal_CompilerErrorMessage_ThrowsException()
    {
        var logger = _loggerFactory.CreateLogger<InheritanceResolver>();

        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(sampleData.systemFake.Builder.Build());
        modelGraph.AppendModel(sampleData.sample_final_fail.Builder.Build());

        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        Assert.Throws<ModelValidationException>(() => inheritanceResolver.Resolve(modelGraph, operationResult));
        
        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.FatalError, operationResult.Messages[0].MessageLevel);
        Assert.Equal(21, operationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void DerivedTypeDefinesFinal_OK()
    {
        var logger = _loggerFactory.CreateLogger<InheritanceResolver>();

        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(sampleData.systemFake.Builder.Build());
        modelGraph.AppendModel(sampleData.sample_final.Builder.Build());

        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        inheritanceResolver.Resolve(modelGraph, operationResult);
        
        Assert.Empty(operationResult.Messages);
    }
    
    [Fact]
    public void TypeNotDerivedFromSystemEntity_CompilerErrorMessage_ThrowsException()
    {
        var logger = _loggerFactory.CreateLogger<InheritanceResolver>();

        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(sampleData.systemFake.Builder.Build());
        modelGraph.AppendModel(sampleData.sample_TypeNotDerivedFromSystemEntity_fail.Builder.Build());

        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        Assert.Throws<ModelValidationException>(() => inheritanceResolver.Resolve(modelGraph, operationResult));

        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.FatalError, operationResult.Messages[0].MessageLevel);
        Assert.Equal(9, operationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void AssociationWithTargetAttributes_OK()
    {
        var logger = _loggerFactory.CreateLogger<InheritanceResolver>();

        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(sampleData.systemFake.Builder.Build());
        modelGraph.AppendModel(sampleData.sample_associationWithAttributes.Builder.Build());

        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        inheritanceResolver.Resolve(modelGraph, operationResult);

        Assert.Empty(operationResult.Messages);
    }
    
    [Fact]
    public void AssociationWithTargetAttributes_NotExistingAttribute_CompilerErrorMessage()
    {
        var logger = _loggerFactory.CreateLogger<InheritanceResolver>();

        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(sampleData.systemFake.Builder.Build());
        modelGraph.AppendModel(sampleData.sample_associationWithAttributes_NotFound_fail.Builder.Build());

        OperationResult operationResult = new();
        InheritanceResolver inheritanceResolver = new(logger);
        inheritanceResolver.Resolve(modelGraph, operationResult);

        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[0].MessageLevel);
        Assert.Equal(47, operationResult.Messages[0].MessageNumber);
    }
}