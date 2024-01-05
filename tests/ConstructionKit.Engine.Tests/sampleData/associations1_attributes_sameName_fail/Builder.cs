using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.associations1_attributes_sameName_fail;

public class Builder
{
    public static CkCompiledModelRoot Build()
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId("TestAssociations", "1.0.0"),
            Dependencies = new List<CkModelId> { new("System", "1.0.0") },
            AssociationRoles = new List<CkAssociationRoleDto>
            {
                new()
                {
                    AssociationRoleId = "Related", InboundMultiplicity = MultiplicitiesDto.N,
                    OutboundMultiplicity = MultiplicitiesDto.N, InboundName = "Related", OutboundName = "Related",
                    Attributes = new List<CkTypeAttributeDto>
                    {
                        new() { CkAttributeId = "TestAssociations/attribute1", AttributeName = "a" },
                        new() { CkAttributeId = "TestAssociations/attribute2", AttributeName = "a" },
                        new() { CkAttributeId = "TestAssociations/attribute3", AttributeName = "c" }
                    }
                },
                new()
                {
                    AssociationRoleId = "Test", InboundMultiplicity = MultiplicitiesDto.One,
                    OutboundMultiplicity = MultiplicitiesDto.N, InboundName = "InboundTest", OutboundName = "OutboundTest",
                    Attributes = new List<CkTypeAttributeDto>
                    {
                        new() { CkAttributeId = "TestAssociations/attribute1", AttributeName = "a" },
                        new() { CkAttributeId = "TestAssociations/attribute2", AttributeName = "b" },
                        new() { CkAttributeId = "TestAssociations/attribute3", AttributeName = "c" },
                        new() { CkAttributeId = "TestAssociations/Record1", AttributeName = "record" }
                    }
                }
            },
            Attributes = new List<CkAttributeDto>
            {
                new()
                {
                    AttributeId = "attribute1",
                    ValueType = AttributeValueTypesDto.String
                },
                new()
                {
                    AttributeId = "attribute2",
                    ValueType = AttributeValueTypesDto.String
                },
                new()
                {
                    AttributeId = "attribute3",
                    ValueType = AttributeValueTypesDto.String
                },
                new()
                {
                    AttributeId = "attribute4",
                    ValueType = AttributeValueTypesDto.String
                },
                new()
                {
                    AttributeId = "attribute5",
                    ValueType = AttributeValueTypesDto.Int
                },
                new()
                {
                    AttributeId = "attribute6",
                    ValueType = AttributeValueTypesDto.Double
                },
                new()
                {
                    AttributeId = "Record1",
                    ValueType = AttributeValueTypesDto.Record,
                    ValueCkRecordId = "TestAssociations/Record1"
                }
            },
            Records = new List<CkRecordDto>
            {
                new()
                {
                    RecordId = "Record1",
                    Attributes = new List<CkTypeAttributeDto>
                    {
                        new() { CkAttributeId = "TestAssociations/attribute1", AttributeName = "a" },
                        new() { CkAttributeId = "TestAssociations/attribute2", AttributeName = "b" },
                        new() { CkAttributeId = "TestAssociations/attribute3", AttributeName = "c" }
                    }
                }
            },
            Types = new List<CkCompiledTypeDto>
            {
                new()
                {
                    TypeId = "Demo1",
                    DerivedFromCkTypeId = "System/Entity",
                    Attributes = new List<CkTypeAttributeDto>
                    {
                        new() { CkAttributeId = "TestAssociations/attribute1", AttributeName = "a" },
                        new() { CkAttributeId = "TestAssociations/attribute2", AttributeName = "b" },
                        new() { CkAttributeId = "TestAssociations/attribute3", AttributeName = "c" },
                        new() { CkAttributeId = "TestAssociations/Record1", AttributeName = "record" }
                    }
                }
            }
        };
    }
}