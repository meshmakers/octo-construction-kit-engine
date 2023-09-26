using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Compiler.SystemTests.sampleData.records1;

public class Builder
{
    public static CkCompiledModelRoot Build()
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId("records1", "1.0.0"),
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
                  ValueType = AttributeValueTypesDto.Int,
              },
              new()
              {
                  AttributeId = "attribute6",
                  ValueType = AttributeValueTypesDto.Double,
              },
              new()
              {
                  AttributeId = "record1",
                  ValueType = AttributeValueTypesDto.Record,
                  ValueCkRecordId = "records1/record1"
              }
            },
            Records = new List<CkRecordDto>
            {
                new()
                {
                    RecordId = "record1",
                    Attributes = new List<CkTypeAttributeDto>
                    {
                        new() { CkAttributeId = "records1/attribute1", AttributeName = "a" },
                        new() { CkAttributeId = "records1/attribute2", AttributeName = "b" },
                        new() { CkAttributeId = "records1/attribute3", AttributeName = "c" }
                    }
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
                        new() { CkAttributeId = "records1/attribute1", AttributeName = "a" },
                        new() { CkAttributeId = "records1/attribute2", AttributeName = "b" },
                        new() { CkAttributeId = "records1/attribute3", AttributeName = "c" }
                    }
                },
                new()
                {
                    TypeId = "Demo2",
                    DerivedFromCkTypeId = "sample1/Demo1",
                    Attributes = new List<CkTypeAttributeDto>
                    {
                        new() { CkAttributeId = "records1/record1", AttributeName = "record" }
                    }
                }
            }
        };
    }
}