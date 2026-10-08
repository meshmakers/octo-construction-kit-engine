using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Resolvers;

/// <summary>
///     CK v2 Phase 0 (AB#5667 / AB#5668): ckLanguage gate (90/91), interface element and reference rules
///     (92-95), implementation rules I-1..I-4 (96-99), inheritance of interfaces and the effective attribute access.
/// </summary>
public class CkV2InterfaceResolverTests(ITestOutputHelper output) : CkV2ResolverTestBase(output)
{
    [Fact]
    public void KitchenSink_ResolvesWithoutMessages_AndInheritsInterfaces()
    {
        var operationResult = new OperationResult();
        var graph = Resolve(Model(), operationResult);

        Assert.True(operationResult.Messages.Count == 0, string.Join(Environment.NewLine, operationResult.Messages));
        var named = graph.Interfaces[$"{M}/Named-1"];
        var serialized = graph.Interfaces[$"{M}/Serialized-1"];

        Assert.Equal(["Named-1"], graph.Types[$"{M}/Principal"].AllImplementedInterfaces.Select(i => i.ElementId.FullName));
        var account = graph.Types[$"{M}/Account"];
        Assert.Equal(["Serialized-1"], account.DeclaredImplements.Select(i => i.ElementId.FullName));
        Assert.Equal(new[] { serialized.CkInterfaceId, named.CkInterfaceId }.OrderBy(i => i),
            account.AllImplementedInterfaces.OrderBy(i => i));

        Assert.Equal(new[] { "Account", "Principal", "Tag" },
            named.ImplementingTypes.Select(t => t.ElementId.Name).OrderBy(n => n));
        Assert.Equal(["Account"], serialized.ImplementingTypes.Select(t => t.ElementId.Name));

        // Members are merged with their attribute definitions.
        Assert.Equal(2, named.Attributes.Count);
        Assert.Equal(AttributeValueTypesDto.String, named.Attributes[$"{M}/Name"].ValueType);
        Assert.True(named.Attributes[$"{M}/Description"].IsOptional);
        Assert.Equal(CkAttributeAccessDto.ReadWrite, named.Attributes[$"{M}/Name"].Access);
    }

    [Fact]
    public void Access_IsResolvedOnTypeAndRecordAssignments_AndInherited()
    {
        var operationResult = new OperationResult();
        var graph = Resolve(Model(), operationResult);

        var account = graph.Types[$"{M}/Account"];
        Assert.Equal(CkAttributeAccessDto.ReadWrite, account.AllAttributesByName["Name"].Access);
        Assert.Equal(CkAttributeAccessDto.MethodOnly, account.AllAttributesByName["Status"].Access);
        Assert.Equal(CkAttributeAccessDto.ReadOnly, account.AllAttributesByName["Serial"].Access);
        Assert.Equal(CkAttributeAccessDto.Hidden, account.AllAttributesByName["PasswordHash"].Access);
        // Undeclared access is ReadWrite.
        Assert.Equal(CkAttributeAccessDto.ReadWrite, graph.Types[$"{M}/Tag"].AllAttributesByName["Name"].Access);

        var address = graph.Records[$"{M}/Address"];
        Assert.Equal(CkAttributeAccessDto.ReadOnly, address.AllAttributesByName["Street"].Access);
        Assert.Equal(CkAttributeAccessDto.Hidden, address.AllAttributesByName["City"].Access);
    }

    [Fact]
    public void MemberSatisfiedByInheritedAttribute_OK()
    {
        var model = Model();
        // Account gets Name from Principal; declaring Named-1 again on Account is satisfied by the inherited member.
        Type(model, "Account").Implements!.Add($"{M}/Named-1");

        ResolveExpectingNoMessages(model);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void Code90_CkV2FeaturesWithoutCkLanguage2(int? ckLanguage)
    {
        var model = Model();
        model.CkLanguage = ckLanguage;

        var messages = ResolveExpectingOnly(model, 90);

        Assert.Contains(messages, m => m.MessageText.Contains("'interfaces'"));
        Assert.Contains(messages, m => m.MessageText.Contains("'implements'"));
        Assert.Contains(messages, m => m.MessageText.Contains("'methods'"));
        Assert.Contains(messages, m => m.MessageText.Contains("'access'") && m.MessageText.Contains("PasswordHash"));
        // record assignments are gated, too
        Assert.Contains(messages, m => m.MessageText.Contains("'access'") && m.MessageText.Contains("City"));
    }

    [Fact]
    public void Code90_AccessOnAssociationRoleAttribute()
    {
        var model = sampleData.sample1.Builder.Build();
        model.AssociationRoles!.Single().Attributes![0].Access = CkAttributeAccessDto.ReadOnly;

        var messages = ResolveExpectingOnly(model, 90);

        Assert.Single(messages);
    }

    [Fact]
    public void Code90_V1ModelWithoutCkV2Features_OK()
    {
        ResolveExpectingNoMessages(sampleData.sample1.Builder.Build());
    }

    [Theory]
    [InlineData(3)]
    [InlineData(0)]
    public void Code91_UnsupportedCkLanguage(int ckLanguage)
    {
        var model = Model();
        model.CkLanguage = ckLanguage;

        var operationResult = new OperationResult();
        Resolve(model, operationResult);

        var message = Assert.Single(operationResult.Messages, m => m.MessageNumber == 91);
        Assert.Contains($"ckLanguage {ckLanguage}", message.MessageText);
    }

    [Fact]
    public void Code92_InterfaceIdNotUnique()
    {
        var model = Model();
        model.Interfaces!.Add(new CkInterfaceDto
        {
            InterfaceId = "Serialized-1",
            Attributes = [new() { CkAttributeId = $"{M}/Serial", AttributeName = "Serial" }]
        });

        var message = Assert.Single(ResolveExpectingOnly(model, 92));
        Assert.Contains("Serialized-1", message.MessageText);
    }

    [Fact]
    public void Code92_SameNameDifferentContractVersion_OK()
    {
        var model = Model();
        model.Interfaces!.Add(new CkInterfaceDto
        {
            InterfaceId = "Serialized-2",
            Attributes = [new() { CkAttributeId = $"{M}/Serial", AttributeName = "Serial" }]
        });

        ResolveExpectingNoMessages(model);
    }

    [Fact]
    public void Code93_InterfaceNameCollidesWithType()
    {
        var model = Model();
        model.Interfaces!.Add(new CkInterfaceDto
        {
            InterfaceId = "Tag-1",
            Attributes = [new() { CkAttributeId = $"{M}/Name", AttributeName = "Name" }]
        });

        var message = Assert.Single(ResolveExpectingOnly(model, 93));
        Assert.Contains("Tag", message.MessageText);
    }

    [Fact]
    public void Code94_InterfaceMemberReferencesUnknownAttribute()
    {
        var model = Model();
        model.Interfaces!.Single(i => i.InterfaceId.Name == "Serialized").Attributes
            .Add(new() { CkAttributeId = $"{M}/DoesNotExist", AttributeName = "DoesNotExist", IsOptional = true });

        var message = Assert.Single(ResolveExpectingOnly(model, 94));
        Assert.Contains("DoesNotExist", message.MessageText);
    }

    // Review L17: duplicate interface members are reported (code 127), not silently dropped.
    [Fact]
    public void Code127_SameAttributeTwiceUnderAnotherName()
    {
        var model = Model();
        model.Interfaces!.Single(i => i.InterfaceId.Name == "Serialized").Attributes
            .Add(new() { CkAttributeId = $"{M}/Serial", AttributeName = "SerialAgain", IsOptional = true });

        var message = Assert.Single(ResolveExpectingOnly(model, 127));
        Assert.Contains("attribute", message.MessageText);
        Assert.Contains("Serial", message.MessageText);
    }

    [Fact]
    public void Code127_TwoMembersWithTheSameName()
    {
        var model = Model();
        model.Interfaces!.Single(i => i.InterfaceId.Name == "Named").Attributes
            .Add(new() { CkAttributeId = $"{M}/Serial", AttributeName = "name", IsOptional = true });

        var message = Assert.Single(ResolveExpectingOnly(model, 127));
        Assert.Contains("name 'name'", message.MessageText);
    }

    [Fact]
    public void Code95_ImplementsUnknownInterface()
    {
        var model = Model();
        Type(model, "Tag").Implements!.Add($"{M}/Unknown-1");

        var message = Assert.Single(ResolveExpectingOnly(model, 95));
        Assert.Contains("Unknown-1", message.MessageText);
        Assert.Contains("Tag", message.MessageText);
    }

    [Fact]
    public void Code96_RequiredMemberMissing()
    {
        var model = Model();
        Type(model, "Tag").Implements!.Add($"{M}/Serialized-1");

        var message = Assert.Single(ResolveExpectingOnly(model, 96));
        Assert.Contains("Serial", message.MessageText);
        Assert.Contains("Serialized-1", message.MessageText);
    }

    [Fact]
    public void Code96_OptionalMemberMayBeMissing_OK()
    {
        var model = Model();
        // Principal does not assign the optional member Description.
        Assert.DoesNotContain(Type(model, "Principal").Attributes!, a => a.AttributeName == "Description");

        ResolveExpectingNoMessages(model);
    }

    [Fact]
    public void Code97_RequiredMemberAssignedAsOptional()
    {
        var model = Model();
        Type(model, "Tag").Attributes!.Single(a => a.AttributeName == "Name").IsOptional = true;

        var message = Assert.Single(ResolveExpectingOnly(model, 97));
        Assert.Contains("Name", message.MessageText);
    }

    [Fact]
    public void Code98_MemberAssignedUnderDifferentName()
    {
        var model = Model();
        Type(model, "Tag").Attributes!.Single(a => a.AttributeName == "Name").AttributeName = "Title";

        var message = Assert.Single(ResolveExpectingOnly(model, 98));
        Assert.Contains("'Title'", message.MessageText);
        Assert.Contains("expected 'Name'", message.MessageText);
    }

    [Fact]
    public void Code99_MemberIsHidden()
    {
        var model = Model();
        Type(model, "Tag").Attributes!.Single(a => a.AttributeName == "Description").Access =
            CkAttributeAccessDto.Hidden;

        var message = Assert.Single(ResolveExpectingOnly(model, 99));
        Assert.Contains("Description", message.MessageText);
    }
}
