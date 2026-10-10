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

    // ── AB#6337 (gate finding H4): why I1 depends on the member's attribute definition, and I2 does not ──

    [Fact]
    public void OptionalAttributeMember_BindsAnExistingAssignment_HiddenOrRenamedBreaksTheImplementor()
    {
        // An optional interface member binds by attribute id: a type that already assigns the definition as Hidden
        // (99) or under another name (98, I-3) breaks when the member is added. That is why the classifier rates an
        // optional member with a previously public definition Major (row I1).
        CkCompiledModelRoot Build(CkTypeAttributeDto assignment)
        {
            var model = Model();
            Add(model, new CkInterfaceDto
            {
                InterfaceId = "Labeled-1",
                Attributes = [new() { CkAttributeId = $"{M}/Street", AttributeName = "Street", IsOptional = true }]
            });
            Type(model, "Tag").Implements = [$"{M}/Labeled-1"];
            Type(model, "Tag").Attributes = [.. Type(model, "Tag").Attributes ?? [], assignment];
            return model;
        }

        Assert.Single(ResolveExpectingOnly(Build(new CkTypeAttributeDto
        {
            CkAttributeId = $"{M}/Street", AttributeName = "Street", IsOptional = true, Access = CkAttributeAccessDto.Hidden
        }), 99));
        Assert.Single(ResolveExpectingOnly(Build(new CkTypeAttributeDto
        {
            CkAttributeId = $"{M}/Street", AttributeName = "Strasse", IsOptional = true
        }), 98));
    }

    [Fact]
    public void I2_OptionalAssociationMember_NeverBindsAnImplementorsAssociation()
    {
        // I2 stays Minor: an optional association member is not checked against implementors (rule 121 skips it), so
        // an implementor that already uses the role with another target or multiplicity keeps compiling.
        ResolveExpectingNoMessages(TagImplementing(
            new CkInterfaceAssociationDto
            {
                CkRoleId = $"{M}/Owns", TargetCkTypeId = $"{M}/Principal", Multiplicity = MultiplicitiesDto.One,
                IsOptional = true
            },
            new CkTypeAssociationDto { CkRoleId = $"{M}/Owns", TargetCkTypeId = $"{M}/Tag" }));
    }

    // ── AB#6336 (gate finding H2): 122 compares only the invocation contract ─────────────

    private static CkMethodDto ContractMethod() => new()
    {
        MethodId = "Relabel-1",
        Description = "relabels",
        Parameters =
        [
            new() { Name = "label", ValueType = AttributeValueTypesDto.String, Description = "the label" },
            new() { Name = "secret", ValueType = AttributeValueTypesDto.String, IsOptional = true, Sensitive = true },
            new() { Name = "mode", ValueType = AttributeValueTypesDto.Enum, ValueCkEnumId = $"{M}/Mode", IsOptional = true }
        ],
        Result = new() { ValueType = AttributeValueTypesDto.Record, ValueCkRecordId = $"{M}/Address" },
        Errors = [new() { Code = "E1", Description = "first" }, new() { Code = "E2" }],
        Authorization = new() { Roles = ["Admin"], AllowSelf = true, Scopes = ["s1"] },
        Execution = new() { TimeoutSeconds = 15, Idempotent = true }
    };

    private static CkCompiledModelRoot Redeclaring(Action<CkMethodDto> mutate)
    {
        var model = Model();
        Add(model, new CkInterfaceDto { InterfaceId = "Labeled-1", Methods = [ContractMethod()] });
        Type(model, "Tag").Implements = [$"{M}/Labeled-1"];
        var redeclared = ContractMethod();
        mutate(redeclared);
        Type(model, "Tag").Methods = [redeclared];
        return model;
    }

    public static TheoryData<string> ContractFields =>
    [
        "kind", "parameter added", "parameter removed", "parameter valueType", "parameter record", "parameter enum",
        "parameter isOptional", "parameter sensitive", "result added", "result removed", "result changed",
        "error added", "error removed"
    ];

    [Theory]
    [MemberData(nameof(ContractFields))]
    public void Code122_ContractFieldDiffers(string field)
    {
        Action<CkMethodDto> mutate = field switch
        {
            "kind" => m => m.Kind = CkMethodKindDto.Static,
            "parameter added" => m => m.Parameters!.Add(new() { Name = "extra", ValueType = AttributeValueTypesDto.String, IsOptional = true }),
            "parameter removed" => m => m.Parameters!.RemoveAt(1),
            "parameter valueType" => m => m.Parameters![0].ValueType = AttributeValueTypesDto.Int,
            "parameter record" => m =>
            {
                m.Parameters![0].ValueType = AttributeValueTypesDto.Record;
                m.Parameters![0].ValueCkRecordId = $"{M}/Address";
            },
            "parameter enum" => m => m.Parameters![2].ValueCkEnumId = "System/Missing",
            "parameter isOptional" => m => m.Parameters![0].IsOptional = true,
            "parameter sensitive" => m => m.Parameters![1].Sensitive = false,
            "result added" => m => m.Result = m.Result,
            "result removed" => m => m.Result = null,
            "result changed" => m => m.Result = new() { ValueType = AttributeValueTypesDto.String },
            "error added" => m => m.Errors!.Add(new() { Code = "E3" }),
            "error removed" => m => m.Errors!.RemoveAt(0),
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        if (field == "result added")
        {
            // The interface method has no result, the redeclaration has one.
            var model = Model();
            var declared = ContractMethod();
            declared.Result = null;
            Add(model, new CkInterfaceDto { InterfaceId = "Labeled-1", Methods = [declared] });
            Type(model, "Tag").Implements = [$"{M}/Labeled-1"];
            Type(model, "Tag").Methods = [ContractMethod()];
            var added = Assert.Single(ResolveExpectingOnly(model, 122));
            Assert.Contains("invocation contract", added.MessageText);
            Assert.Contains("result added", added.MessageText);
            return;
        }

        var operationResult = new OperationResult();
        Resolve(Redeclaring(mutate), operationResult);
        var message = Assert.Single(operationResult.Messages, m => m.MessageNumber == 122);
        Assert.Contains("invocation contract", message.MessageText);
    }

    [Fact]
    public void Code122_MetadataAndOrder_MayDiffer()
    {
        // Gate cases X4 (timeout), X6 (roles), X7 (idempotent) and 7i/7j (order): no 122.
        ResolveExpectingNoMessages(Redeclaring(m =>
        {
            m.Description = "other";
            m.Parameters![0].Description = "other";
            m.Errors![0].Description = "other";
            m.Authorization = new() { Roles = ["Admin", "Ops"], AllowSelf = false, Scopes = [] };
            m.Execution = new() { TimeoutSeconds = 45, Idempotent = false };
            m.Parameters.Reverse();
            m.Errors.Reverse();
        }));
        ResolveExpectingNoMessages(Redeclaring(m =>
        {
            m.Authorization = null;
            m.Execution = null;
        }));
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
