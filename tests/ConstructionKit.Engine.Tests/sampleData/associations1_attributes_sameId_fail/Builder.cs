using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.associations1_attributes_sameId_fail;

public class Builder
{
    public static CkCompiledModelRoot Build()
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId("TestAssociations", "1.0.0"),
            Dependencies = [new("System", "1.0.0")],
            AssociationRoles =
            [
                new()
                {
                    AssociationRoleId = "Related", InboundMultiplicity = MultiplicitiesDto.N,
                    OutboundMultiplicity = MultiplicitiesDto.N, InboundName = "Related", OutboundName = "Related",
                    Attributes =
                    [
                        new() { CkAttributeId = "TestAssociations/Attribute1", AttributeName = "A" },
                        new() { CkAttributeId = "TestAssociations/Attribute1", AttributeName = "B" },
                        new() { CkAttributeId = "TestAssociations/Attribute3", AttributeName = "C" }
                    ]
                },

                new()
                {
                    AssociationRoleId = "Test", InboundMultiplicity = MultiplicitiesDto.One,
                    OutboundMultiplicity = MultiplicitiesDto.N, InboundName = "InboundTest",
                    OutboundName = "OutboundTest",
                    Attributes =
                    [
                        new() { CkAttributeId = "TestAssociations/Attribute1", AttributeName = "A" },
                        new() { CkAttributeId = "TestAssociations/Attribute2", AttributeName = "B" },
                        new() { CkAttributeId = "TestAssociations/Attribute3", AttributeName = "C" },
                        new() { CkAttributeId = "TestAssociations/Record1", AttributeName = "Record" }
                    ]
                }
            ],
            Attributes =
            [
                new()
                {
                    AttributeId = "Attribute1",
                    ValueType = AttributeValueTypesDto.String
                },

                new()
                {
                    AttributeId = "Attribute2",
                    ValueType = AttributeValueTypesDto.String
                },

                new()
                {
                    AttributeId = "Attribute3",
                    ValueType = AttributeValueTypesDto.String
                },

                new()
                {
                    AttributeId = "Attribute4",
                    ValueType = AttributeValueTypesDto.String
                },

                new()
                {
                    AttributeId = "Attribute5",
                    ValueType = AttributeValueTypesDto.Int
                },

                new()
                {
                    AttributeId = "Attribute6",
                    ValueType = AttributeValueTypesDto.Double
                },

                new()
                {
                    AttributeId = "Record1",
                    ValueType = AttributeValueTypesDto.Record,
                    ValueCkRecordId = "TestAssociations/Record1"
                }
            ],
            Records =
            [
                new()
                {
                    RecordId = "Record1",
                    Attributes =
                    [
                        new() { CkAttributeId = "TestAssociations/Attribute1", AttributeName = "A" },
                        new() { CkAttributeId = "TestAssociations/Attribute2", AttributeName = "B" },
                        new() { CkAttributeId = "TestAssociations/Attribute3", AttributeName = "C" }
                    ]
                }
            ],
            Types =
            [
                new()
                {
                    TypeId = "Demo1",
                    DerivedFromCkTypeId = "System/Entity",
                    Attributes =
                    [
                        new() { CkAttributeId = "TestAssociations/Attribute1", AttributeName = "A" },
                        new() { CkAttributeId = "TestAssociations/Attribute2", AttributeName = "B" },
                        new() { CkAttributeId = "TestAssociations/Attribute3", AttributeName = "C" },
                        new() { CkAttributeId = "TestAssociations/Record1", AttributeName = "Record" }
                    ]
                }
            ]
        };
    }
}
