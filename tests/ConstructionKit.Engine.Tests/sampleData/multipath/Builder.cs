using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.multipath;

/// <summary>
///     Multi-path scenario: Root type has two different outbound associations
///     that both lead to the same Target type through different intermediate types.
///     Root --LinkA--> Middle1 --LinkC--> Target (has Name attribute)
///     Root --LinkB--> Middle2 --LinkC--> Target (has Name attribute)
///     Without the fix for shared ignoredNavigations, only the first path to Target
///     would generate column paths; the second would be skipped.
/// </summary>
public class Builder
{
    public static CkCompiledModelRoot Build()
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId("MultiPath", "1.0.0"),
            Dependencies = [new("System", "1.0.0")],
            AssociationRoles =
            [
                new()
                {
                    AssociationRoleId = "LinkA",
                    InboundMultiplicity = MultiplicitiesDto.N,
                    OutboundMultiplicity = MultiplicitiesDto.ZeroOrOne,
                    InboundName = "LinkedByA",
                    OutboundName = "LinkA"
                },
                new()
                {
                    AssociationRoleId = "LinkB",
                    InboundMultiplicity = MultiplicitiesDto.N,
                    OutboundMultiplicity = MultiplicitiesDto.ZeroOrOne,
                    InboundName = "LinkedByB",
                    OutboundName = "LinkB"
                },
                new()
                {
                    AssociationRoleId = "LinkC",
                    InboundMultiplicity = MultiplicitiesDto.N,
                    OutboundMultiplicity = MultiplicitiesDto.ZeroOrOne,
                    InboundName = "LinkedByC",
                    OutboundName = "LinkC"
                }
            ],
            Attributes =
            [
                new() { AttributeId = "Name", ValueType = AttributeValueTypesDto.String }
            ],
            Types =
            [
                new()
                {
                    TypeId = "Target",
                    DerivedFromCkTypeId = "System/Entity",
                    Attributes =
                    [
                        new() { CkAttributeId = "MultiPath/Name", AttributeName = "Name" }
                    ]
                },
                new()
                {
                    TypeId = "Middle1",
                    DerivedFromCkTypeId = "System/Entity",
                    Associations =
                    [
                        new() { CkRoleId = "MultiPath/LinkC", TargetCkTypeId = "MultiPath/Target" }
                    ]
                },
                new()
                {
                    TypeId = "Middle2",
                    DerivedFromCkTypeId = "System/Entity",
                    Associations =
                    [
                        new() { CkRoleId = "MultiPath/LinkC", TargetCkTypeId = "MultiPath/Target" }
                    ]
                },
                new()
                {
                    TypeId = "Root",
                    DerivedFromCkTypeId = "System/Entity",
                    Associations =
                    [
                        new() { CkRoleId = "MultiPath/LinkA", TargetCkTypeId = "MultiPath/Middle1" },
                        new() { CkRoleId = "MultiPath/LinkB", TargetCkTypeId = "MultiPath/Middle2" }
                    ]
                }
            ]
        };
    }
}
