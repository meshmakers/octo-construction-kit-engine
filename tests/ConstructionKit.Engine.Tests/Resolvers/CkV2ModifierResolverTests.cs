using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Resolvers;

/// <summary>
///     F1.1-S4 (AB#5907): <c>visibility</c> / <c>derivable</c> are CK v2 keys (gate 90) and resolve to effective
///     values on the graph. <c>visibility</c> defaults to <c>Public</c>; <c>derivable</c> defaults to <c>Any</c> in
///     a v1 model and to <c>Model</c> in a <c>ckLanguage: 2</c> model.
/// </summary>
public class CkV2ModifierResolverTests(ITestOutputHelper output) : CkV2ResolverTestBase(output)
{
    [Fact]
    public void V1Model_EffectiveDefaults_ArePublicAndAny()
    {
        var operationResult = new OperationResult();
        var graph = Resolve(sampleData.sample1.Builder.Build(), operationResult);
        Assert.Empty(operationResult.Messages);

        Assert.All(graph.Types.Values.Where(t => t.CkTypeId.ModelId.Name == "sample1"), t =>
        {
            Assert.Equal(CkVisibilityDto.Public, t.Visibility);
            Assert.Equal(CkDerivableDto.Any, t.Derivable);
        });
        Assert.All(graph.Records.Values.Where(r => r.CkRecordId.ModelId.Name == "sample1"), r =>
        {
            Assert.Equal(CkVisibilityDto.Public, r.Visibility);
            Assert.Equal(CkDerivableDto.Any, r.Derivable);
        });
        Assert.All(graph.Attributes.Values, a => Assert.Equal(CkVisibilityDto.Public, a.Visibility));
        Assert.All(graph.AssociationRoles.Values, r => Assert.Equal(CkVisibilityDto.Public, r.Visibility));
    }

    [Fact]
    public void V2Model_DerivableDefaultsToModel_VisibilityToPublic()
    {
        var operationResult = new OperationResult();
        var graph = Resolve(Model(), operationResult);
        Assert.Empty(operationResult.Messages);

        foreach (var name in new[] { "Principal", "Account", "Tag" })
        {
            var type = graph.Types[$"{M}/{name}"];
            Assert.Equal(CkVisibilityDto.Public, type.Visibility);
            Assert.Equal(CkDerivableDto.Model, type.Derivable);
        }

        Assert.Equal(CkDerivableDto.Model, graph.Records[$"{M}/Address"].Derivable);
        Assert.Equal(CkVisibilityDto.Public, graph.Enums[$"{M}/Mode"].Visibility);
        Assert.Equal(CkVisibilityDto.Public, graph.Interfaces[$"{M}/Named-1"].Visibility);
        // A dependency keeps its own (v1) defaults.
        Assert.Equal(CkDerivableDto.Any, graph.Types["System/Entity"].Derivable);
    }

    [Fact]
    public void V2Model_ExplicitModifiers_AreResolved()
    {
        var model = Model();
        Type(model, "Tag").Visibility = CkVisibilityDto.Internal;
        Type(model, "Tag").Derivable = CkDerivableDto.Any;
        Type(model, "Account").Methods![0].Visibility = CkVisibilityDto.Internal;
        model.Records!.Single().Visibility = CkVisibilityDto.Internal;
        model.Records!.Single().Derivable = CkDerivableDto.Any;
        model.Enums!.Single().Visibility = CkVisibilityDto.Internal;
        model.Attributes!.Single(a => a.AttributeId == "PasswordHash").Visibility = CkVisibilityDto.Internal;
        model.Interfaces!.Single(i => i.InterfaceId == "Named-1").Visibility = CkVisibilityDto.Internal;
        // AB#6334 (129): Principal and Account reference the internal record, enum, attribute and interface, so they
        // must be internal themselves.
        Type(model, "Principal").Visibility = CkVisibilityDto.Internal;
        Type(model, "Account").Visibility = CkVisibilityDto.Internal;
        model.Attributes!.Single(a => a.ValueCkRecordId != null).Visibility = CkVisibilityDto.Internal;

        var operationResult = new OperationResult();
        var graph = Resolve(model, operationResult);
        Assert.Empty(operationResult.Messages);

        Assert.Equal(CkVisibilityDto.Internal, graph.Types[$"{M}/Tag"].Visibility);
        Assert.Equal(CkDerivableDto.Any, graph.Types[$"{M}/Tag"].Derivable);
        Assert.Equal(CkVisibilityDto.Internal,
            graph.Types[$"{M}/Account"].DefinedMethods.Single(m => m.MethodId == Type(model, "Account").Methods![0].MethodId).Visibility);
        Assert.Equal(CkVisibilityDto.Internal, graph.Records[$"{M}/Address"].Visibility);
        Assert.Equal(CkDerivableDto.Any, graph.Records[$"{M}/Address"].Derivable);
        Assert.Equal(CkVisibilityDto.Internal, graph.Enums[$"{M}/Mode"].Visibility);
        Assert.Equal(CkVisibilityDto.Internal, graph.Attributes[$"{M}/PasswordHash"].Visibility);
        Assert.Equal(CkVisibilityDto.Public, graph.Attributes[$"{M}/Name"].Visibility);
        Assert.Equal(CkVisibilityDto.Internal, graph.Interfaces[$"{M}/Named-1"].Visibility);
    }

    [Fact]
    public void V2Model_AssociationRoleVisibility_IsResolved()
    {
        var model = sampleData.sample1.Builder.Build();
        model.CkLanguage = 2;
        model.AssociationRoles!.Single().Visibility = CkVisibilityDto.Internal;
        // AB#6334 (129): the type that uses the internal role must be internal, too.
        model.Types!.Single(t => t.TypeId == "Demo3").Visibility = CkVisibilityDto.Internal;

        var operationResult = new OperationResult();
        var graph = Resolve(model, operationResult);
        Assert.Empty(operationResult.Messages);

        Assert.Equal(CkVisibilityDto.Internal, graph.AssociationRoles["sample1/Related"].Visibility);
        Assert.Equal(CkDerivableDto.Model, graph.Types["sample1/Demo1"].Derivable);
    }

    public static TheoryData<string> GatedElements => ["Type", "TypeDerivable", "Record", "RecordDerivable", "Attribute", "AssociationRole"];

    [Theory]
    [MemberData(nameof(GatedElements))]
    public void Code90_ModifiersInV1Model(string element)
    {
        var model = sampleData.sample1.Builder.Build();
        string expectedFeature;
        string expectedElement;
        switch (element)
        {
            case "Type":
                model.Types!.Single(t => t.TypeId == "Demo1").Visibility = CkVisibilityDto.Internal;
                (expectedFeature, expectedElement) = ("visibility", "/Demo1");
                break;
            case "TypeDerivable":
                model.Types!.Single(t => t.TypeId == "Demo1").Derivable = CkDerivableDto.Any;
                (expectedFeature, expectedElement) = ("derivable", "/Demo1");
                break;
            case "Record":
                model.Records!.Single().Visibility = CkVisibilityDto.Public;
                (expectedFeature, expectedElement) = ("visibility", "/Record1");
                break;
            case "RecordDerivable":
                model.Records!.Single().Derivable = CkDerivableDto.Model;
                (expectedFeature, expectedElement) = ("derivable", "/Record1");
                break;
            case "Attribute":
                var attribute = model.Attributes![0];
                attribute.Visibility = CkVisibilityDto.Internal;
                (expectedFeature, expectedElement) = ("visibility", $"/{attribute.AttributeId}");
                break;
            default:
                model.AssociationRoles!.Single().Visibility = CkVisibilityDto.Internal;
                (expectedFeature, expectedElement) = ("visibility", "/Related");
                break;
        }

        var message = Assert.Single(ResolveExpectingOnly(model, 90));
        Assert.Contains($"'{expectedFeature}'", message.MessageText);
        Assert.Contains(expectedElement, message.MessageText);
    }

    [Fact]
    public void Code90_EnumAndMethodVisibilityInV1Model()
    {
        var model = Model();
        model.CkLanguage = 1;
        model.Enums!.Single().Visibility = CkVisibilityDto.Internal;
        Type(model, "Account").Methods![0].Visibility = CkVisibilityDto.Internal;

        var messages = ResolveExpectingOnly(model, 90);

        Assert.Contains(messages, m => m.MessageText.Contains("'visibility'") && m.MessageText.Contains("/Mode"));
        Assert.Contains(messages, m => m.MessageText.Contains("'visibility'") &&
                                       m.MessageText.Contains($"/Account-1.{Type(model, "Account").Methods![0].MethodId}"));
    }
}
