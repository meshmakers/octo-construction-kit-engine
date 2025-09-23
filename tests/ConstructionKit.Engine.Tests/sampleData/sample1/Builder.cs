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
                        new() { CkAttributeId = "sample1/attribute4", AttributeName = "d" },
                        new() { CkAttributeId = "sample1/attribute5", AttributeName = "e" },
                        new() { CkAttributeId = "sample1/attribute6", AttributeName = "f" }
                    ]
                }
            ],
            Attributes =
            [
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
                    ValueType = AttributeValueTypesDto.String
                },

                new()
                {
                    AttributeId = "attribute6",
                    ValueType = AttributeValueTypesDto.String
                },

                new()
                {
                    AttributeId = "attribute7",
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
                        new() { CkAttributeId = "sample1/attribute1", AttributeName = "a" },
                        new() { CkAttributeId = "sample1/attribute2", AttributeName = "b" },
                        new() { CkAttributeId = "sample1/attribute3", AttributeName = "c" }
                    ]
                },

                new()
                {
                    TypeId = "Demo2",
                    DerivedFromCkTypeId = "sample1/Demo1",
                    Attributes =
                    [
                        new() { CkAttributeId = "sample1/attribute4", AttributeName = "d" },
                        new() { CkAttributeId = "sample1/attribute5", AttributeName = "e" },
                        new() { CkAttributeId = "sample1/attribute6", AttributeName = "f" },
                        new() { CkAttributeId = "sample1/attribute7", AttributeName = "g" }
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
                        new() { CkAttributeId = "sample1/attribute1", AttributeName = "a" },
                        new() { CkAttributeId = "sample1/attribute2", AttributeName = "b" },
                        new() { CkAttributeId = "sample1/attribute3", AttributeName = "c" }
                    ]
                }
            ]
        };
    }
}