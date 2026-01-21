using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.sample_attributes_sameNameAtInheritance_fail;

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
                        new() { CkAttributeId = "sample1/Attribute4", AttributeName = "A" },
                        new() { CkAttributeId = "sample1/Attribute5", AttributeName = "E" },
                        new() { CkAttributeId = "sample1/Attribute6", AttributeName = "F" }
                    ],
                    Associations = [new() { CkRoleId = "System/ParentChild", TargetCkTypeId = "sample1/Demo1" }]
                },

                new()
                {
                    TypeId = "Demo3",
                    DerivedFromCkTypeId = "sample1/Demo2",
                    Associations = [new() { CkRoleId = "sample1/Related", TargetCkTypeId = "System/Entity" }]
                }
            ]
        };
    }
}
