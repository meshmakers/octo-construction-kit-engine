using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests.sampleData.sample_assocs_sameRoleIdDifferentTrees_ok;

public class Builder
{
    public static CkCompiledModelRoot Build()
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId("sample1", "1.0.0"),
            Dependencies = new List<CkModelId> { new("System", "1.0.0") },
            AssociationRoles = new List<CkAssociationRoleDto>
            {
                new()
                {
                    AssociationRoleId = "Related", InboundMultiplicity = MultiplicitiesDto.N,
                    OutboundMultiplicity = MultiplicitiesDto.N, InboundName = "Related", OutboundName = "Related"
                }
            },
            Attributes = new List<CkAttributeDto>
            {
              new()
              {
                  AttributeId = "attribute1",
                  ValueType = AttributeValueTypesDto.String,
              },
              new()
              {
                  AttributeId = "attribute2",
                  ValueType = AttributeValueTypesDto.String,
              },
              new()
              {
                  AttributeId = "attribute3",
                  ValueType = AttributeValueTypesDto.String,
              },
              new()
              {
                  AttributeId = "attribute4",
                  ValueType = AttributeValueTypesDto.String,
              },
              new()
              {
                  AttributeId = "attribute5",
                  ValueType = AttributeValueTypesDto.String,
              },
              new()
              {
                  AttributeId = "attribute6",
                  ValueType = AttributeValueTypesDto.String,
              },
              new()
              {
                  AttributeId = "attribute7",
                  ValueType = AttributeValueTypesDto.String,
              }
            },
            Types = new List<CkTypeDto>
            {
                new()
                {
                    TypeId = "Alpha1",
                    DerivedFromCkTypeId = "System/Entity",
                    Attributes = new List<CkTypeAttributeDto>
                    {
                        new() { CkAttributeId = "sample1/attribute1", AttributeName = "a" },
                        new() { CkAttributeId = "sample1/attribute2", AttributeName = "b" },
                        new() { CkAttributeId = "sample1/attribute3", AttributeName = "c" }
                    }
                },
                new()
                {
                    TypeId = "Alpha2",
                    DerivedFromCkTypeId = "sample1/Alpha1",
                    Attributes = new List<CkTypeAttributeDto>
                    {
                        new() { CkAttributeId = "sample1/attribute4", AttributeName = "d" },
                        new() { CkAttributeId = "sample1/attribute5", AttributeName = "e" },
                        new() { CkAttributeId = "sample1/attribute6", AttributeName = "f" }
                    }
                },
                new()
                {
                    TypeId = "Alpha3",
                    DerivedFromCkTypeId = "sample1/Alpha2",
                    Associations = new List<CkTypeAssociationDto>
                    {
                        new() { CkRoleId = "sample1/Related", TargetCkTypeId = "sample1/Alpha1" }, // here
                    }
                    
                },
                
                new()
                {
                    TypeId = "Beta1",
                    DerivedFromCkTypeId = "System/Entity",
                    Attributes = new List<CkTypeAttributeDto>
                    {
                        new() { CkAttributeId = "sample1/attribute1", AttributeName = "a" },
                        new() { CkAttributeId = "sample1/attribute2", AttributeName = "b" },
                        new() { CkAttributeId = "sample1/attribute3", AttributeName = "c" }
                    }
                },
                new()
                {
                    TypeId = "Beta2",
                    DerivedFromCkTypeId = "sample1/Beta1",
                    Attributes = new List<CkTypeAttributeDto>
                    {
                        new() { CkAttributeId = "sample1/attribute4", AttributeName = "d" },
                        new() { CkAttributeId = "sample1/attribute5", AttributeName = "e" },
                        new() { CkAttributeId = "sample1/attribute6", AttributeName = "f" }
                    }
                },
                new()
                {
                    TypeId = "Beta3",
                    DerivedFromCkTypeId = "sample1/Beta2",
                    Associations = new List<CkTypeAssociationDto>
                    {
                        new() { CkRoleId = "sample1/Related", TargetCkTypeId = "sample1/Beta2" }, // here
                    }
                    
                }
            },
        };
    }
}