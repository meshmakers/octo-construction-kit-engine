using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.Runtime.Engine.Tests.sampleData.models;

public class Builder
{
    public static RtModelRootDto Build()
    {
        return new RtModelRootDto
        {
            Entities =
            {
                new RtEntityDto
                {
                    RtId = OctoObjectId.GenerateNewId(),
                    CkTypeId = "Sample2/Sample2Demo2",
                    Attributes =
                    {
                        new RtAttributeDto { Id = "sample1/attribute1", Value = "a" },
                        new RtAttributeDto { Id = "sample2/attributeA", Value = "b" },
                        new RtAttributeDto { Id = "sample3/attributeB", Value = "c" }
                    }
                },
                new RtEntityDto
                {
                    RtId = OctoObjectId.GenerateNewId(),
                    CkTypeId = "Sample2/Demo2",
                    Attributes =
                    {
                        new RtAttributeDto { Id = "sample1/attributeC", Value = "d" },
                        new RtAttributeDto { Id = "sample1/attributeD", Value = "e" },
                        new RtAttributeDto { Id = "sample1/attributeE", Value = "f" }
                    }
                }
            }
        };
    }
}