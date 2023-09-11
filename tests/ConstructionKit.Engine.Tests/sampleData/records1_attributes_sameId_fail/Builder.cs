using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.records1_attributes_sameId_fail;

public class Builder
{
    public static CkCompiledModelRoot Build()
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId("TestRecords", "1.0.0"),
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
                  AttributeId = "Record1",
                  ValueType = AttributeValueTypesDto.Record,
                  ValueCkRecordId = "TestRecords/Record1"
              }
            },
            Records = new List<CkRecordDto>
            {
                new()
                {
                    RecordId = "Record1",
                    Attributes = new List<CkTypeAttributeDto>
                    {
                        new() { CkAttributeId = "TestRecords/attribute1", AttributeName = "a" },
                        new() { CkAttributeId = "TestRecords/attribute1", AttributeName = "b" },
                        new() { CkAttributeId = "TestRecords/attribute3", AttributeName = "c" }
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
                        new() { CkAttributeId = "TestRecords/attribute1", AttributeName = "a" },
                        new() { CkAttributeId = "TestRecords/attribute2", AttributeName = "b" },
                        new() { CkAttributeId = "TestRecords/attribute3", AttributeName = "c" },
                        new() { CkAttributeId = "TestRecords/Record1", AttributeName = "record" }

                    }
                }
            }
        };
    }
}