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
            Dependencies = [new("System", "[1.0,)")],
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
                    ValueCkRecordId = "records1/Record1"
                }
            ],
            Records =
            [
                new()
                {
                    RecordId = "Record1",
                    Attributes =
                    [
                        new() { CkAttributeId = "records1/Attribute1", AttributeName = "A" },
                        new() { CkAttributeId = "records1/Attribute2", AttributeName = "B" },
                        new() { CkAttributeId = "records1/Attribute3", AttributeName = "C" }
                    ]
                }
            ],
            Types =
            [
                new()
                {
                    TypeId = "Demo1",
                    DerivedFromCkTypeId = "${System}/Entity",
                    Attributes =
                    [
                        new() { CkAttributeId = "records1/Attribute1", AttributeName = "A" },
                        new() { CkAttributeId = "records1/Attribute2", AttributeName = "B" },
                        new() { CkAttributeId = "records1/Attribute3", AttributeName = "C" }
                    ]
                },

                new()
                {
                    TypeId = "Demo2",
                    DerivedFromCkTypeId = "sample1/Demo1",
                    Attributes = [new() { CkAttributeId = "records1/Record1", AttributeName = "Record" }]
                }
            ]
        };
    }
}
