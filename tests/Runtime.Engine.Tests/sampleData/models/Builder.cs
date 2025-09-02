using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;

namespace Meshmakers.Octo.Runtime.Engine.Tests.sampleData.models;

public class Builder
{
    public static RtModelRootTcDto Build()
    {
        return new RtModelRootTcDto
        {
            Entities =
            {
                new RtEntityTcDto
                {
                    RtId = OctoObjectId.GenerateNewId(),
                    CkTypeId = "Sample2/Sample2Demo2",
                    Attributes =
                    {
                        new RtAttributeTcDto { Id = "sample1/attribute1", Value = "a" },
                        new RtAttributeTcDto { Id = "sample2/attributeA", Value = "b" },
                        new RtAttributeTcDto { Id = "sample3/attributeB", Value = "c" }
                    }
                },
                new RtEntityTcDto
                {
                    RtId = OctoObjectId.GenerateNewId(),
                    CkTypeId = "Sample2/Demo2",
                    Attributes =
                    {
                        new RtAttributeTcDto { Id = "sample1/attributeC", Value = "d" },
                        new RtAttributeTcDto { Id = "sample1/attributeD", Value = "e" },
                        new RtAttributeTcDto { Id = "sample1/attributeE", Value = "f" }
                    }
                }
            }
        };
    }
}