using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Resolvers;

/// <summary>
///     F1.2-S4 (AB#5913): compiler rules for interface <c>extends</c> (118, 119), association members (120, 121),
///     interface methods (100-104 as for types, 122), empty interfaces (123), deprecation warnings (124) and a type
///     association narrowed to an unknown interface (128). One failing and one passing case per rule.
/// </summary>
public class CkV2InterfaceValidationTests(ITestOutputHelper output) : CkV2ResolverTestBase(output)
{
    private static CkCompiledModelRoot WithRole()
    {
        var model = Model();
        model.AssociationRoles =
        [
            new CkAssociationRoleDto
            {
                AssociationRoleId = "Owns", InboundName = "OwnedBy", OutboundName = "Owns",
                InboundMultiplicity = MultiplicitiesDto.N, OutboundMultiplicity = MultiplicitiesDto.N
            }
        ];
        return model;
    }

    private static CkInterfaceDto Add(CkCompiledModelRoot model, CkInterfaceDto ckInterface)
    {
        model.Interfaces!.Add(ckInterface);
        return ckInterface;
    }

    private static CkInterfaceDto Labeled(params CkInterfaceAssociationDto[] associations) => new()
    {
        InterfaceId = "Labeled-1",
        Attributes = [new() { CkAttributeId = $"{M}/Description", AttributeName = "Description", IsOptional = true }],
        Associations = associations.Length == 0 ? null : associations.ToList()
    };

    // ── 118 extends ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Code118_UnknownExtends()
    {
        var model = Model();
        Add(model, new CkInterfaceDto { InterfaceId = "Labeled-1", Extends = [$"{M}/Missing-1"] });

        var message = Assert.Single(ResolveExpectingOnly(model, 118));
        Assert.Contains("unknown", message.MessageText);
    }

    [Fact]
    public void Code118_SelfAndCycle()
    {
        var model = Model();
        Add(model, new CkInterfaceDto { InterfaceId = "Self-1", Extends = [$"{M}/Self-1"] });
        Add(model, new CkInterfaceDto { InterfaceId = "Ping-1", Extends = [$"{M}/Pong-1"] });
        Add(model, new CkInterfaceDto { InterfaceId = "Pong-1", Extends = [$"{M}/Ping-1"] });

        var messages = ResolveExpectingOnly(model, 118);

        Assert.Contains(messages, m => m.MessageText.Contains("itself"));
        Assert.Equal(2, messages.Count(m => m.MessageText.Contains("cycle")));
    }

    [Fact]
    public void Extends_Valid_OK()
    {
        var model = Model();
        Add(model, new CkInterfaceDto { InterfaceId = "Labeled-1", Extends = [$"{M}/Named-1", $"{M}/Serialized-1"] });

        ResolveExpectingNoMessages(model);
    }

    // ── 119 member conflicts ──────────────────────────────────────────────────────────

    [Fact]
    public void Code119_SameNameForAnotherAttribute()
    {
        var model = Model();
        Add(model, new CkInterfaceDto
        {
            InterfaceId = "Labeled-1", Extends = [$"{M}/Named-1"],
            Attributes = [new() { CkAttributeId = $"{M}/Serial", AttributeName = "Name" }]
        });

        var message = Assert.Single(ResolveExpectingOnly(model, 119));
        Assert.Contains("'Name'", message.MessageText);
    }

    [Fact]
    public void Code119_SameAttributeUnderAnotherName()
    {
        var model = Model();
        Add(model, new CkInterfaceDto
        {
            InterfaceId = "Labeled-1", Extends = [$"{M}/Named-1"],
            Attributes = [new() { CkAttributeId = $"{M}/Name", AttributeName = "Title" }]
        });

        Assert.Single(ResolveExpectingOnly(model, 119));
    }

    [Fact]
    public void RepeatingAnInheritedMemberIdentically_OK()
    {
        var model = Model();
        Add(model, new CkInterfaceDto
        {
            InterfaceId = "Labeled-1", Extends = [$"{M}/Named-1"],
            Attributes = [new() { CkAttributeId = $"{M}/Name", AttributeName = "Name" }]
        });

        ResolveExpectingNoMessages(model);
    }

    // ── 120 association members ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("unknown role")]
    [InlineData("no target")]
    [InlineData("two targets")]
    [InlineData("unknown target type")]
    [InlineData("unknown target interface")]
    public void Code120_InvalidAssociationMember(string variant)
    {
        var model = WithRole();
        var member = new CkInterfaceAssociationDto { CkRoleId = $"{M}/Owns", TargetCkTypeId = $"{M}/Tag", IsOptional = true };
        switch (variant)
        {
            case "unknown role": member.CkRoleId = $"{M}/Nope"; break;
            case "no target": member.TargetCkTypeId = null; break;
            case "two targets": member.TargetCkInterfaceId = $"{M}/Named-1"; break;
            case "unknown target type": member.TargetCkTypeId = $"{M}/Nope"; break;
            default: member.TargetCkTypeId = null; member.TargetCkInterfaceId = $"{M}/Nope-1"; break;
        }

        Add(model, Labeled(member));

        Assert.Single(ResolveExpectingOnly(model, 120));
    }

    // ── 121 association satisfaction ──────────────────────────────────────────────────

    private CkCompiledModelRoot TagImplementing(CkInterfaceAssociationDto member, CkTypeAssociationDto? provided)
    {
        var model = WithRole();
        Add(model, Labeled(member));
        Type(model, "Tag").Implements = [$"{M}/Labeled-1"];
        if (provided != null)
        {
            Type(model, "Tag").Associations = [provided];
        }

        return model;
    }

    [Fact]
    public void Code121_RequiredAssociationMemberMissing()
    {
        var model = TagImplementing(new CkInterfaceAssociationDto { CkRoleId = $"{M}/Owns", TargetCkTypeId = $"{M}/Tag" }, null);

        var message = Assert.Single(ResolveExpectingOnly(model, 121));
        Assert.Contains("Owns", message.MessageText);
    }

    [Fact]
    public void RequiredAssociationMemberProvided_OK_OptionalMayBeMissing_OK()
    {
        ResolveExpectingNoMessages(TagImplementing(
            new CkInterfaceAssociationDto { CkRoleId = $"{M}/Owns", TargetCkTypeId = $"{M}/Tag" },
            new CkTypeAssociationDto { CkRoleId = $"{M}/Owns", TargetCkTypeId = $"{M}/Tag" }));
        ResolveExpectingNoMessages(TagImplementing(
            new CkInterfaceAssociationDto { CkRoleId = $"{M}/Owns", TargetCkTypeId = $"{M}/Tag", IsOptional = true }, null));
    }

    [Fact]
    public void Code121_MultiplicityTooPermissive()
    {
        // The role is N to N; the member requires exactly One.
        var model = TagImplementing(
            new CkInterfaceAssociationDto { CkRoleId = $"{M}/Owns", TargetCkTypeId = $"{M}/Tag", Multiplicity = MultiplicitiesDto.One },
            new CkTypeAssociationDto { CkRoleId = $"{M}/Owns", TargetCkTypeId = $"{M}/Tag" });

        var message = Assert.Single(ResolveExpectingOnly(model, 121));
        Assert.Contains("multiplicity One", message.MessageText);
    }

    [Fact]
    public void InterfaceTarget_SatisfiedByAnImplementor_NotByAnotherType()
    {
        var member = new CkInterfaceAssociationDto { CkRoleId = $"{M}/Owns", TargetCkInterfaceId = $"{M}/Named-1" };
        // Account derives from Principal, which implements Named-1.
        ResolveExpectingNoMessages(TagImplementing(member,
            new CkTypeAssociationDto { CkRoleId = $"{M}/Owns", TargetCkTypeId = $"{M}/Account" }));

        Assert.Single(ResolveExpectingOnly(TagImplementing(member,
            new CkTypeAssociationDto { CkRoleId = $"{M}/Owns", TargetCkTypeId = $"{M}/Tag" }), 121));

        // ...or by an association narrowed to the interface.
        ResolveExpectingNoMessages(TagImplementing(member, new CkTypeAssociationDto
        {
            CkRoleId = $"{M}/Owns", TargetCkTypeId = "System/Entity", TargetCkInterfaceId = $"{M}/Named-1"
        }));
    }

    // ── interface methods (100-104, 122) ─────────────────────────────────────────────

    [Fact]
    public void InterfaceMethods_FollowTheMethodRules()
    {
        var model = Model();
        Add(model, new CkInterfaceDto
        {
            InterfaceId = "Labeled-1",
            Methods = [new() { MethodId = "Create-1" }, new() { MethodId = "Relabel-1" }, new() { MethodId = "Relabel-1" }]
        });

        var operationResult = new OperationResult();
        Resolve(model, operationResult);

        Assert.Contains(operationResult.Messages, m => m.MessageNumber == 102);
        Assert.Contains(operationResult.Messages, m => m.MessageNumber == 100);
    }

    [Fact]
    public void InterfaceMethod_IsInheritedByTheImplementingType()
    {
        var model = Model();
        Add(model, new CkInterfaceDto { InterfaceId = "Labeled-1", Methods = [new() { MethodId = "Relabel-1" }] });
        Type(model, "Tag").Implements = [$"{M}/Labeled-1"];
        var operationResult = new OperationResult();

        var graph = Resolve(model, operationResult);

        Assert.Empty(operationResult.Messages);
        Assert.Equal(graph.Types[$"{M}/Tag"].CkTypeId, graph.Types[$"{M}/Tag"].AllMethods["Relabel-1"].DeclaringCkTypeId);
    }

    [Fact]
    public void Code122_RedeclaredWithAnotherSignature_SameSignatureOK()
    {
        CkCompiledModelRoot Build(CkMethodKindDto kind)
        {
            var model = Model();
            Add(model, new CkInterfaceDto { InterfaceId = "Labeled-1", Methods = [new() { MethodId = "Relabel-1" }] });
            Type(model, "Tag").Implements = [$"{M}/Labeled-1"];
            Type(model, "Tag").Methods = [new() { MethodId = "Relabel-1", Kind = kind }];
            return model;
        }

        Assert.Single(ResolveExpectingOnly(Build(CkMethodKindDto.Static), 122));
        ResolveExpectingNoMessages(Build(CkMethodKindDto.Instance));
    }

    // ── 123 empty interface ───────────────────────────────────────────────────────────

    [Fact]
    public void Code123_InterfaceWithoutMembers()
    {
        var model = Model();
        Add(model, new CkInterfaceDto { InterfaceId = "Empty-1" });

        Assert.Single(ResolveExpectingOnly(model, 123));
    }

    // ── 124 deprecated ────────────────────────────────────────────────────────────────

    [Fact]
    public void Code124_DeprecationIsAWarning()
    {
        var model = Model();
        model.Interfaces!.Single(i => i.InterfaceId.FullName == "Serialized-1").Deprecated = true;
        Add(model, new CkInterfaceDto { InterfaceId = "Labeled-1", Extends = [$"{M}/Serialized-1"] });
        var operationResult = new OperationResult();

        Resolve(model, operationResult);

        // Account implements it, Labeled-1 extends it.
        Assert.Equal(2, operationResult.Messages.Count);
        Assert.All(operationResult.Messages, m =>
        {
            Assert.Equal(124, m.MessageNumber);
            Assert.Equal(MessageLevel.Warning, m.MessageLevel);
        });
        Assert.False(operationResult.HasErrors);
    }

    // ── 128 narrowed association target ───────────────────────────────────────────────

    [Fact]
    public void Code128_AssociationNarrowedToAnUnknownInterface()
    {
        var model = WithRole();
        Type(model, "Tag").Associations =
            [new() { CkRoleId = $"{M}/Owns", TargetCkTypeId = $"{M}/Tag", TargetCkInterfaceId = $"{M}/Nope-1" }];

        Assert.Single(ResolveExpectingOnly(model, 128));
    }
}
