using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.records1_recordIdDoesNotExist;

public class Builder
{
    public static CkCompiledModelRoot Build()
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId("TestRecords", "1.0.0"),
            Dependencies = [new("System", "1.0.0")],
            AssociationRoles =
            [
                new()
                {
                    AssociationRoleId = "Related", InboundMultiplicity = MultiplicitiesDto.N,
                    OutboundMultiplicity = MultiplicitiesDto.N, InboundName = "Related", OutboundName = "Related"
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
                    ValueCkRecordId = "records1/Record2" // Error here - record does not exist
                }
            ],
            Records =
            [
                new()
                {
                    RecordId = "Record1",
                    Attributes =
                    [
                        new() { CkAttributeId = "TestRecords/Attribute1", AttributeName = "A" },
                        new() { CkAttributeId = "TestRecords/Attribute2", AttributeName = "B" },
                        new() { CkAttributeId = "TestRecords/Attribute3", AttributeName = "C" }
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
                        new() { CkAttributeId = "TestRecords/Attribute1", AttributeName = "A" },
                        new() { CkAttributeId = "TestRecords/Attribute2", AttributeName = "B" },
                        new() { CkAttributeId = "TestRecords/Attribute3", AttributeName = "C" },
                        new() { CkAttributeId = "TestRecords/Record1", AttributeName = "Record" }
                    ]
                }
            ]
        };
    }
}
