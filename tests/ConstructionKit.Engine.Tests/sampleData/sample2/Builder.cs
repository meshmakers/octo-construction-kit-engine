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
            Dependencies = new List<CkModelId> { new("System", "1.0.0"), new ("sample1", "1.0.0") },
            Attributes = new List<CkAttributeDto>
            {
              new()
              {
                  AttributeId = "attributeA",
                  ValueType = AttributeValueTypesDto.String,
              },
              new()
              {
                  AttributeId = "attributeB",
                  ValueType = AttributeValueTypesDto.String,
              },
              new()
              {
                  AttributeId = "attributeC",
                  ValueType = AttributeValueTypesDto.String,
              },
              new()
              {
                  AttributeId = "attributeD",
                  ValueType = AttributeValueTypesDto.String,
              },
              new()
              {
                  AttributeId = "attributeE",
                  ValueType = AttributeValueTypesDto.String,
              },
              new()
              {
                  AttributeId = "attributeF",
                  ValueType = AttributeValueTypesDto.String,
              },
              new()
              {
                  AttributeId = "attributeG",
                  ValueType = AttributeValueTypesDto.String,
              }
            },
            Types = new List<CkTypeDto>
            {
                new()
                {
                    TypeId = "Sample2Demo2",
                    DerivedFromCkTypeId = "System/Entity",
                    Attributes = new List<CkTypeAttributeDto>
                    {
                        new() { CkAttributeId = "sample1/attribute1", AttributeName = "a" },
                        new() { CkAttributeId = "sample2/attributeA", AttributeName = "b" },
                        new() { CkAttributeId = "sample3/attributeB", AttributeName = "c" }
                    }
                },
                new()
                {
                    TypeId = "Demo2",
                    DerivedFromCkTypeId = "sample1/Demo1",
                    Attributes = new List<CkTypeAttributeDto>
                    {
                        new() { CkAttributeId = "sample1/attributeC", AttributeName = "d" },
                        new() { CkAttributeId = "sample1/attributeD", AttributeName = "e" },
                        new() { CkAttributeId = "sample1/attributeE", AttributeName = "f" }
                    }
                }
            },
        };
    }
}