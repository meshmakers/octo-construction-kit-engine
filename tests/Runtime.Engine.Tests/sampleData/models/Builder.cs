using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.Runtime.Engine.Tests.sampleData.models
{
    public class Builder
    {
        public static RtModelRootDto Build()
        {
            return new RtModelRootDto
            {
                Entities =
                {
                    new()
                    {
                        RtId = OctoObjectId.GenerateNewId(),
                        CkTypeId = "Sample2/Sample2Demo2",
                        Attributes = 
                        {
                            new() { Id = "sample1/attribute1", Value = "a" },
                            new() { Id = "sample2/attributeA", Value = "b" },
                            new() { Id = "sample3/attributeB", Value = "c" }
                        }
                    },
                    new()
                    {
                        RtId = OctoObjectId.GenerateNewId(),
                        CkTypeId = "Sample2/Demo2",
                        Attributes = 
                        {
                            new() { Id = "sample1/attributeC", Value = "d" },
                            new() { Id = "sample1/attributeD", Value = "e" },
                            new() { Id = "sample1/attributeE", Value = "f" }
                        }
                    }
                },
            };
        }
    }
}