using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.sample1;

public class Builder
{
    public static CkCompiledModelRoot Build()
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId("sample1", "1.0.0"),
            Dependencies = [new("System", "1.0.0")],
            AssociationRoles =
            [
                new()
                {
                    AssociationRoleId = "Related", InboundMultiplicity = MultiplicitiesDto.N,
                    OutboundMultiplicity = MultiplicitiesDto.N, InboundName = "Related", OutboundName = "Related",
                    Attributes =
                    [
                        new() { CkAttributeId = "sample1/Attribute4", AttributeName = "D" },
                        new() { CkAttributeId = "sample1/Attribute5", AttributeName = "E" },
                        new() { CkAttributeId = "sample1/Attribute6", AttributeName = "F" }
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
                    ValueType = AttributeValueTypesDto.String
                },

                new()
                {
                    AttributeId = "Attribute6",
                    ValueType = AttributeValueTypesDto.String
                },

                new()
                {
                    AttributeId = "Attribute7",
                    ValueType = AttributeValueTypesDto.String
                },

                new()
                {
                    AttributeId = "Record1",
                    ValueType = AttributeValueTypesDto.Record,
                    ValueCkRecordId = "sample1/Record1"
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
                        new() { CkAttributeId = "sample1/Attribute1", AttributeName = "A" },
                        new() { CkAttributeId = "sample1/Attribute2", AttributeName = "B" },
                        new() { CkAttributeId = "sample1/Attribute3", AttributeName = "C" }
                    ]
                },

                new()
                {
                    TypeId = "Demo2",
                    DerivedFromCkTypeId = "sample1/Demo1",
                    Attributes =
                    [
                        new() { CkAttributeId = "sample1/Attribute4", AttributeName = "D" },
                        new() { CkAttributeId = "sample1/Attribute5", AttributeName = "E" },
                        new() { CkAttributeId = "sample1/Attribute6", AttributeName = "F" },
                        new() { CkAttributeId = "sample1/Attribute7", AttributeName = "G" }
                    ],
                    Associations = [new() { CkRoleId = "System/ParentChild", TargetCkTypeId = "sample1/Demo1" }]
                },

                new()
                {
                    TypeId = "Demo3",
                    DerivedFromCkTypeId = "sample1/Demo2",
                    Associations = [new() { CkRoleId = "sample1/Related", TargetCkTypeId = "System/Entity" }]
                }
            ],
            Records =
            [
                new()
                {
                    RecordId = "Record1",
                    Attributes =
                    [
                        new() { CkAttributeId = "sample1/Attribute1", AttributeName = "A" },
                        new() { CkAttributeId = "sample1/Attribute2", AttributeName = "B" },
                        new() { CkAttributeId = "sample1/Attribute3", AttributeName = "C" }
                    ]
                }
            ]
        };
    }
}