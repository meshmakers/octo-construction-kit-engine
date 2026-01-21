using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.enums_enumIdDoesNotExist;

public class Builder
{
    public static CkCompiledModelRoot Build()
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId("TestEnums", "1.0.0"),
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
                    AttributeId = "Enum1",
                    ValueType = AttributeValueTypesDto.Enum,
                    ValueCkEnumId = "TestEnums/Enum2" // Error here - enum does not exist
                }
            ],
            Enums =
            [
                new()
                {
                    EnumId = "Enum1",
                    Values = new List<CkEnumValueDto>
                    {
                        new() { Key = 0, Name = "ValueA" },
                        new() { Key = 1, Name = "ValueB" },
                        new() { Key = 2, Name = "ValueC" }
                    }
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
                        new() { CkAttributeId = "TestEnums/Attribute1", AttributeName = "A" },
                        new() { CkAttributeId = "TestEnums/Attribute2", AttributeName = "B" },
                        new() { CkAttributeId = "TestEnums/Attribute3", AttributeName = "C" },
                        new() { CkAttributeId = "TestEnums/Enum1", AttributeName = "Enum" }
                    ]
                }
            ]
        };
    }
}
