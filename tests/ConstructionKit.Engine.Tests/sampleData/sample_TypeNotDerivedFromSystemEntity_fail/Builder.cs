using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.sample_TypeNotDerivedFromSystemEntity_fail;

public class Builder
{
    public static CkCompiledModelRoot Build()
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId("sample1", "1.0.0"),
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
                    ValueType = AttributeValueTypesDto.String,
                }
            },
            Types = new List<CkCompiledTypeDto>
            {
                new()
                {
                    TypeId = "Demo1",
                    Attributes = new List<CkTypeAttributeDto>
                    {
                        new() { CkAttributeId = "sample1/attribute1", AttributeName = "a" }
                    }
                }
            },
        };
    }
}