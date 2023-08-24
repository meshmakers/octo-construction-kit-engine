using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests.sampleData.systemFake;

public class Builder
{
    public static CkCompiledModelRoot Build()
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId("System", "1.0.0"),
            Types = new List<CkTypeDto>
            {
                new()
                {
                    TypeId = "Entity",
                    IsAbstract = true
                }
            },
            AssociationRoles = new List<CkAssociationRoleDto>
            {
                new()
                {
                    AssociationRoleId = "ParentChild", InboundMultiplicity = MultiplicitiesDto.One,
                    OutboundMultiplicity = MultiplicitiesDto.N, InboundName = "Parent", OutboundName = "Children"
                }
            }
        };
    }
}