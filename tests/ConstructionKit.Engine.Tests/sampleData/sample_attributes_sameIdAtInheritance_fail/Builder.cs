using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests.sampleData.sample_attributes_sameIdAtInheritance_fail;

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
                    TypeId = "Demo1",
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
                    TypeId = "Demo2",
                    DerivedFromCkTypeId = "sample1/Demo1",
                    Attributes = new List<CkTypeAttributeDto>
                    {
                        new() { CkAttributeId = "sample1/attribute3", AttributeName = "d" },
                        new() { CkAttributeId = "sample1/attribute5", AttributeName = "e" },
                        new() { CkAttributeId = "sample1/attribute6", AttributeName = "f" }
                    },
                    Associations = new List<CkTypeAssociationDto>
                    {
                        new() { CkRoleId = "System/ParentChild", TargetCkTypeId = "sample1/Demo1" },
                    }
                    
                },
                new()
                {
                    TypeId = "Demo3",
                    DerivedFromCkTypeId = "sample1/Demo2",
                    Associations = new List<CkTypeAssociationDto>
                    {
                        new() { CkRoleId = "sample1/Related", TargetCkTypeId = "System/Entity" },
                    }
                    
                }
            },
        };
    }
}