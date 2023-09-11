using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Resolvers;

public class ElementResolverTests
{
    [Fact]
    public void Resolve_ValidInput_ReturnsCkModelGraph()
    {
        var ckModelRoot = sampleData.sample1.Builder.Build();
        
        var resolver = new ElementResolver();
        var validationResult = new OperationResult();

        var result = resolver.Resolve(ckModelRoot, validationResult);

        Assert.IsType<CkModelGraph>(result);
    }

    [Fact]
    public void Resolve_InvalidAttributeName_AddsErrorMessage()
    {
        var ckModelRoot = sampleData.sample1.Builder.Build();
        if (ckModelRoot.Attributes != null)
        {
            ckModelRoot.Attributes[0].AttributeId = "Invalid_Attribute_Name!";
        }

        var resolver = new ElementResolver();
        var validationResult = new OperationResult();
    
        resolver.Resolve(ckModelRoot, validationResult);
    
        Assert.Single(validationResult.Messages);
        Assert.Equal(MessageLevel.Error, validationResult.Messages[0].MessageLevel);
        Assert.Equal(25, validationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public void Resolve_InvalidAssociationRoleId_AddsErrorMessage()
    {
        var ckModelRoot = sampleData.sample1.Builder.Build();
        if (ckModelRoot.AssociationRoles != null)
        {
            ckModelRoot.AssociationRoles[0].AssociationRoleId = "Invalid_Assoc_Role!";
        }

        var resolver = new ElementResolver();
        var validationResult = new OperationResult();
    
        resolver.Resolve(ckModelRoot, validationResult);
    
        Assert.Single(validationResult.Messages);
        Assert.Equal(MessageLevel.Error, validationResult.Messages[0].MessageLevel);
        Assert.Equal(26, validationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void Resolve_InvalidTypeId_AddsErrorMessage()
    {
        var ckModelRoot = sampleData.sample1.Builder.Build();
        if (ckModelRoot.Types != null)
        {
            ckModelRoot.Types[0].TypeId = "Invalid_TypeId!";
        }

        var resolver = new ElementResolver();
        var validationResult = new OperationResult();
    
        resolver.Resolve(ckModelRoot, validationResult);
    
        Assert.Single(validationResult.Messages);
        Assert.Equal(MessageLevel.Error, validationResult.Messages[0].MessageLevel);
        Assert.Equal(24, validationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void Resolve_MultipleTypes_AddsErrorMessage()
    {
        var ckModelRoot = sampleData.sample1.Builder.Build();
        if (ckModelRoot.Types != null)
        {
            ckModelRoot.Types.Add(new CkTypeDto{TypeId = "Demo1"});
        }

        var resolver = new ElementResolver();
        var validationResult = new OperationResult();
    
        resolver.Resolve(ckModelRoot, validationResult);
    
        Assert.Single(validationResult.Messages);
        Assert.Equal(MessageLevel.Error, validationResult.Messages[0].MessageLevel);
        Assert.Equal(8, validationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void Resolve_MultipleAttributes_AddsErrorMessage()
    {
        var ckModelRoot = sampleData.sample1.Builder.Build();
        if (ckModelRoot.Attributes != null)
        {
            ckModelRoot.Attributes.Add(new CkAttributeDto{AttributeId = "Demo1"});
            ckModelRoot.Attributes.Add(new CkAttributeDto{AttributeId = "Demo1"});
        }

        var resolver = new ElementResolver();
        var validationResult = new OperationResult();
    
        resolver.Resolve(ckModelRoot, validationResult);
    
        Assert.Single(validationResult.Messages);
        Assert.Equal(MessageLevel.Error, validationResult.Messages[0].MessageLevel);
        Assert.Equal(6, validationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void Resolve_MultipleAssociations_AddsErrorMessage()
    {
        var ckModelRoot = sampleData.sample1.Builder.Build();
        ckModelRoot.AssociationRoles = new List<CkAssociationRoleDto>
        {
            new() { AssociationRoleId = "Assoc1" },
            new() { AssociationRoleId = "Assoc1" }
        };

        var resolver = new ElementResolver();
        var validationResult = new OperationResult();
    
        resolver.Resolve(ckModelRoot, validationResult);
    
        Assert.Single(validationResult.Messages);
        Assert.Equal(MessageLevel.Error, validationResult.Messages[0].MessageLevel);
        Assert.Equal(7, validationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void Resolve_RecordAttributes_RecordTypeWithOutCkRecordId_Fails()
    {
        var ckModelRoot =sampleData.sample1. Builder.Build();
        if (ckModelRoot.Attributes != null)
        {
            ckModelRoot.Attributes.Add(new CkAttributeDto{AttributeId = "Demo1", ValueType = AttributeValueTypesDto.Record,
                ValueCkRecordId = null});
        }

        var resolver = new ElementResolver();
        var validationResult = new OperationResult();
    
        resolver.Resolve(ckModelRoot, validationResult);
    
        Assert.Single(validationResult.Messages);
        Assert.Equal(MessageLevel.Error, validationResult.Messages[0].MessageLevel);
        Assert.Equal(31, validationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void Resolve_InvalidRecord_AddsErrorMessage()
    {
        var ckModelRoot = sampleData.sample1.Builder.Build();
        if (ckModelRoot.Records != null)
        {
            ckModelRoot.Records.Add(new CkRecordDto{RecordId = "Invalid_Record_Name!"});
        }

        var resolver = new ElementResolver();
        var validationResult = new OperationResult();
    
        resolver.Resolve(ckModelRoot, validationResult);
    
        Assert.Single(validationResult.Messages);
        Assert.Equal(MessageLevel.Error, validationResult.Messages[0].MessageLevel);
        Assert.Equal(32, validationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void Resolve_MultipleRecords_AddsErrorMessage()
    {
        var ckModelRoot = sampleData.sample1.Builder.Build();
        if (ckModelRoot.Records != null)
        {
            ckModelRoot.Records.Add(new CkRecordDto{RecordId = "Demo1"});
            ckModelRoot.Records.Add(new CkRecordDto{RecordId = "Demo1"});
        }

        var resolver = new ElementResolver();
        var validationResult = new OperationResult();
    
        resolver.Resolve(ckModelRoot, validationResult);
    
        Assert.Single(validationResult.Messages);
        Assert.Equal(MessageLevel.Error, validationResult.Messages[0].MessageLevel);
        Assert.Equal(33, validationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void Resolve_CkType_AttributesSameId_OK()
    {
        var compiledModelRoot = sampleData.sample1.Builder.Build();
        
        OperationResult operationResult = new();
        var resolver = new ElementResolver();
        resolver.Resolve(compiledModelRoot, operationResult);

        Assert.Empty(operationResult.Messages);
    }
    
    [Fact]
    public void Resolve_CkType_AttributesSameId_CompilerErrorMessage()
    {
        var compiledModelRoot = sampleData.sample_attributes_sameId_fail.Builder.Build();
        
        OperationResult operationResult = new();
        var resolver = new ElementResolver();
        resolver.Resolve(compiledModelRoot, operationResult);

        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[0].MessageLevel);
        Assert.Equal(16, operationResult.Messages[0].MessageNumber);
    }
        
    [Fact]
    public void Resolve_CkType_AttributesSameName_CompilerErrorMessage()
    {
        var compiledModelRoot = sampleData.sample_attributes_sameName_fail.Builder.Build();

        OperationResult operationResult = new();
        var resolver = new ElementResolver();
        resolver.Resolve(compiledModelRoot, operationResult);

        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[0].MessageLevel);
        Assert.Equal(15, operationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void Resolve_CkRecord_AttributesSameId_OK()
    {
        var compiledModelRoot = sampleData.records1.Builder.Build();
        
        OperationResult operationResult = new();
        var resolver = new ElementResolver();
        resolver.Resolve(compiledModelRoot, operationResult);

        Assert.Empty(operationResult.Messages);
    }
    
    [Fact]
    public void Resolve_CkRecord_AttributesSameId_CompilerErrorMessage()
    {
        var compiledModelRoot = sampleData.records1_attributes_sameId_fail.Builder.Build();
        
        OperationResult operationResult = new();
        var resolver = new ElementResolver();
        resolver.Resolve(compiledModelRoot, operationResult);

        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[0].MessageLevel);
        Assert.Equal(39, operationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void Resolve_CkRecord_AttributesSameName_CompilerErrorMessage()
    {
        var compiledModelRoot = sampleData.records1_attributes_sameName_fail.Builder.Build();

        OperationResult operationResult = new();
        var resolver = new ElementResolver();
        resolver.Resolve(compiledModelRoot, operationResult);

        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[0].MessageLevel);
        Assert.Equal(37, operationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public void Resolve_CkAssociation_AttributesSameId_OK()
    {
        var compiledModelRoot = sampleData.associations1.Builder.Build();
        
        OperationResult operationResult = new();
        var resolver = new ElementResolver();
        resolver.Resolve(compiledModelRoot, operationResult);

        Assert.Empty(operationResult.Messages);
    }
    
    [Fact]
    public void Resolve_CkAssociation_AttributesSameId_CompilerErrorMessage()
    {
        var compiledModelRoot = sampleData.associations1_attributes_sameId_fail.Builder.Build();
        
        OperationResult operationResult = new();
        var resolver = new ElementResolver();
        resolver.Resolve(compiledModelRoot, operationResult);

        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[0].MessageLevel);
        Assert.Equal(50, operationResult.Messages[0].MessageNumber);
    }
        
    [Fact]
    public void Resolve_CkCkAssociation_AttributesSameName_CompilerErrorMessage()
    {
        var compiledModelRoot = sampleData.associations1_attributes_sameName_fail.Builder.Build();

        OperationResult operationResult = new();
        var resolver = new ElementResolver();
        resolver.Resolve(compiledModelRoot, operationResult);

        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[0].MessageLevel);
        Assert.Equal(49, operationResult.Messages[0].MessageNumber);
    }
}