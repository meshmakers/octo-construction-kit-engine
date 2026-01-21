using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.sample2;

public class Builder
{
    public static CkCompiledModelRoot Build()
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId("sample2", "1.0.0"),
            Dependencies = [new("System", "1.0.0"), new("sample1", "1.0.0")],
            Attributes =
            [
                new()
                {
                    AttributeId = "AttributeA",
                    ValueType = AttributeValueTypesDto.String
                },

                new()
                {
                    AttributeId = "AttributeB",
                    ValueType = AttributeValueTypesDto.String
                },

                new()
                {
                    AttributeId = "AttributeC",
                    ValueType = AttributeValueTypesDto.String
                },

                new()
                {
                    AttributeId = "AttributeD",
                    ValueType = AttributeValueTypesDto.String
                },

                new()
                {
                    AttributeId = "AttributeE",
                    ValueType = AttributeValueTypesDto.String
                },

                new()
                {
                    AttributeId = "AttributeF",
                    ValueType = AttributeValueTypesDto.String
                },

                new()
                {
                    AttributeId = "AttributeG",
                    ValueType = AttributeValueTypesDto.String
                }
            ],
            Types =
            [
                new()
                {
                    TypeId = "Sample2Demo2",
                    DerivedFromCkTypeId = "System/Entity",
                    Attributes =
                    [
                        new() { CkAttributeId = "sample1/Attribute1", AttributeName = "A" },
                        new() { CkAttributeId = "sample2/AttributeA", AttributeName = "B" },
                        new() { CkAttributeId = "sample3/AttributeB", AttributeName = "C" }
                    ]
                },

                new()
                {
                    TypeId = "Demo2",
                    DerivedFromCkTypeId = "sample1/Demo1",
                    Attributes =
                    [
                        new() { CkAttributeId = "sample1/AttributeC", AttributeName = "D" },
                        new() { CkAttributeId = "sample1/AttributeD", AttributeName = "E" },
                        new() { CkAttributeId = "sample1/AttributeE", AttributeName = "F" }
                    ]
                }
            ]
        };
    }
}
