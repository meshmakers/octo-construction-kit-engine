using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.enums_enumIdNotDefined;

public class Builder
{
    public static CkCompiledModelRoot Build()
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId("TestEnums", "1.0.0"),
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
                    AttributeId = "Enum1",
                    ValueType = AttributeValueTypesDto.Enum,
                  //  ValueCkEnumId = "TestEnums/Enum2" // Error here - enum not defined
                }
            },
            Enums = new List<CkEnumDto>
            {
                new()
                {
                    EnumId = "Enum1",
                    Values = new List<CkEnumValueDto>
                    {
                        new() { Key = 0, Name = "a" },
                        new() { Key = 1, Name = "b" },
                        new() { Key = 2, Name = "c" }
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
                        new() { CkAttributeId = "TestEnums/attribute1", AttributeName = "a" },
                        new() { CkAttributeId = "TestEnums/attribute2", AttributeName = "b" },
                        new() { CkAttributeId = "TestEnums/attribute3", AttributeName = "c" },
                        new() { CkAttributeId = "TestEnums/Enum1", AttributeName = "enum" }
                    }
                }
            }
        };
    }
}