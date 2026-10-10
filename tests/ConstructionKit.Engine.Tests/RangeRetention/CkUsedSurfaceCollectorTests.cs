using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.RangeRetention;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.RangeRetention;

/// <summary>
///     AB#4472: one test per reference kind of the usedSurface collector (element level; member-level paths are
///     covered with a resolved graph in the compiler tests).
/// </summary>
public class CkUsedSurfaceCollectorTests
{
    private const string Own = "Dep";

    private static CkCompiledModelRoot Model(Action<CkCompiledModelRoot> configure)
    {
        var model = new CkCompiledModelRoot
        {
            ModelId = new CkModelId(Own, "1.0.0"),
            DependencyRanges =
            [
                new CkModelDependencyDto { Range = "System-[2.5,3.0)", Floor = "2.5.0" },
                new CkModelDependencyDto { Range = "Basic-[1.0,2.0)", Floor = "1.0.0" }
            ]
        };
        configure(model);
        CkUsedSurfaceCollector.Apply(model, null);
        return model;
    }

    private static List<string> System(CkCompiledModelRoot model) =>
        model.DependencyRanges!.Single(d => d.Range.Name == "System").UsedSurface!;

    private static CkTypeAttributeDto Assignment(string id) => new() { CkAttributeId = id, AttributeName = "A", IsOptional = true };

    public static TheoryData<string, string> ReferenceKinds() => new()
    {
        { "base type", "System@2/Entity-1" },
        { "implemented interface", "System@2/Named-1" },
        { "interface extends", "System@2/Named-1" },
        { "type attribute assignment", "System@2/Name-1" },
        { "record attribute assignment", "System@2/Name-1" },
        { "role attribute assignment", "System@2/Name-1" },
        { "interface member attribute", "System@2/Name-1" },
        { "record base", "System@2/Address-1" },
        { "attribute record value type", "System@2/Address-1" },
        { "attribute enum value type", "System@2/Mode-1" },
        { "method parameter record", "System@2/Address-1" },
        { "method result enum", "System@2/Mode-1" },
        { "association role", "System@2/Owns-1" },
        { "association target type", "System@2/Entity-1" },
        { "association target interface", "System@2/Named-1" },
        { "association target attribute", "System@2/Name-1" }
    };

    [Theory]
    [MemberData(nameof(ReferenceKinds))]
    public void EveryReferenceKind_IsListedAtElementLevel(string kind, string expected)
    {
        var model = Model(m =>
        {
            switch (kind)
            {
                case "base type":
                    m.Types = [new CkCompiledTypeDto { TypeId = "Thing", DerivedFromCkTypeId = "System@2/Entity-1" }];
                    break;
                case "implemented interface":
                    m.Types = [new CkCompiledTypeDto { TypeId = "Thing", Implements = [new CkId<CkInterfaceId>("System@2/Named-1")] }];
                    break;
                case "interface extends":
                    m.Interfaces = [new CkInterfaceDto { InterfaceId = "Own-1", Extends = [new CkId<CkInterfaceId>("System@2/Named-1")] }];
                    break;
                case "type attribute assignment":
                    m.Types = [new CkCompiledTypeDto { TypeId = "Thing", Attributes = [Assignment("System@2/Name-1")] }];
                    break;
                case "record attribute assignment":
                    m.Records = [new CkRecordDto { RecordId = "Rec", Attributes = [Assignment("System@2/Name-1")] }];
                    break;
                case "role attribute assignment":
                    m.AssociationRoles = [new CkAssociationRoleDto { AssociationRoleId = "Role", InboundName = "I", OutboundName = "O", Attributes = [Assignment("System@2/Name-1")] }];
                    break;
                case "interface member attribute":
                    m.Interfaces = [new CkInterfaceDto { InterfaceId = "Own-1", Attributes = [new CkInterfaceAttributeDto { CkAttributeId = "System@2/Name-1", AttributeName = "Name" }] }];
                    break;
                case "record base":
                    m.Records = [new CkRecordDto { RecordId = "Rec", DerivedFromCkRecordId = "System@2/Address-1" }];
                    break;
                case "attribute record value type":
                    m.Attributes = [new CkAttributeDto { AttributeId = "Home", ValueType = AttributeValueTypesDto.Record, ValueCkRecordId = "System@2/Address-1" }];
                    break;
                case "attribute enum value type":
                    m.Attributes = [new CkAttributeDto { AttributeId = "Mode", ValueType = AttributeValueTypesDto.Enum, ValueCkEnumId = "System@2/Mode-1" }];
                    break;
                case "method parameter record":
                    m.Types = [new CkCompiledTypeDto { TypeId = "Thing", Methods = [new CkMethodDto { MethodId = "Do-1", Parameters = [new CkMethodParameterDto { Name = "p", ValueType = AttributeValueTypesDto.Record, ValueCkRecordId = "System@2/Address-1" }] }] }];
                    break;
                case "method result enum":
                    m.Interfaces = [new CkInterfaceDto { InterfaceId = "Own-1", Methods = [new CkMethodDto { MethodId = "Do-1", Result = new CkMethodResultDto { ValueType = AttributeValueTypesDto.Enum, ValueCkEnumId = "System@2/Mode-1" } }] }];
                    break;
                case "association role":
                    m.Types = [new CkCompiledTypeDto { TypeId = "Thing", Associations = [new CkTypeAssociationDto { CkRoleId = "System@2/Owns-1", TargetCkTypeId = "Dep-1.0.0/Thing-1" }] }];
                    break;
                case "association target type":
                    m.Types = [new CkCompiledTypeDto { TypeId = "Thing", Associations = [new CkTypeAssociationDto { CkRoleId = "Dep-1.0.0/Role-1", TargetCkTypeId = "System@2/Entity-1" }] }];
                    break;
                case "association target interface":
                    m.Types = [new CkCompiledTypeDto { TypeId = "Thing", Associations = [new CkTypeAssociationDto { CkRoleId = "Dep-1.0.0/Role-1", TargetCkTypeId = "Dep-1.0.0/Thing-1", TargetCkInterfaceId = "System@2/Named-1" }] }];
                    break;
                case "association target attribute":
                    m.Types = [new CkCompiledTypeDto { TypeId = "Thing", Associations = [new CkTypeAssociationDto { CkRoleId = "Dep-1.0.0/Role-1", TargetCkTypeId = "Dep-1.0.0/Thing-1", TargetCkAttributeIds = ["System@2/Name-1"] }] }];
                    break;
            }
        });

        Assert.Equal([expected], System(model));
        Assert.Empty(model.DependencyRanges!.Single(d => d.Range.Name == "Basic").UsedSurface!);
    }

    [Fact]
    public void OwnReferences_AreNeverListed_AndEachDependencyGetsItsOwnSurface()
    {
        var model = Model(m =>
        {
            m.Attributes = [new CkAttributeDto { AttributeId = "Code", ValueType = AttributeValueTypesDto.String }];
            m.Types =
            [
                new CkCompiledTypeDto
                {
                    TypeId = "Thing", DerivedFromCkTypeId = "Basic@1/Asset-1",
                    Attributes = [Assignment("Dep-1.0.0/Code-1"), Assignment("System@2/Name-1"), Assignment("System@2/Name-1")]
                }
            ];
        });

        Assert.Equal(["System@2/Name-1"], System(model));
        Assert.Equal(["Basic@1/Asset-1"], model.DependencyRanges!.Single(d => d.Range.Name == "Basic").UsedSurface);
    }

    [Fact]
    public void Hash_IsSha256OverTheSortedList_AndStable()
    {
        var model = Model(m => m.Types = [new CkCompiledTypeDto { TypeId = "Thing", DerivedFromCkTypeId = "System@2/Entity-1", Implements = [new CkId<CkInterfaceId>("System@2/Named-1")] }]);
        var dependency = model.DependencyRanges!.Single(d => d.Range.Name == "System");

        Assert.Equal(["System@2/Entity-1", "System@2/Named-1"], dependency.UsedSurface);
        Assert.Equal(CkUsedSurfaceCollector.Hash(["System@2/Entity-1", "System@2/Named-1"]), dependency.UsedSurfaceHash);
        Assert.NotEqual(CkUsedSurfaceCollector.Hash(["System@2/Named-1", "System@2/Entity-1"]), dependency.UsedSurfaceHash);
        Assert.Equal("sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", CkUsedSurfaceCollector.Hash([]));
    }
}
