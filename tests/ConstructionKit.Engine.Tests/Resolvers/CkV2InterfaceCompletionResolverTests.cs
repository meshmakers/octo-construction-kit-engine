using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.RangeRetention;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Resolvers;

/// <summary>
///     F1.1-S5 (AB#5908): interface <c>extends</c>, association and method members, <c>deprecated</c>, and
///     <c>targetCkInterfaceId</c> on type associations — definitions and graph resolution. The validation rules
///     (unknown references, cycles, member conformance) are F1.2-S4.
/// </summary>
public class CkV2InterfaceCompletionResolverTests(ITestOutputHelper output) : CkV2ResolverTestBase(output)
{
    /// <summary>
    ///     Kitchen sink plus <c>Labeled-1</c> (extends <c>Named-1</c>, adds <c>Serial</c>, a ParentChild association
    ///     member and a method) and <c>Tagged-1</c> (extends <c>Labeled-1</c>, deprecated).
    /// </summary>
    private static CkCompiledModelRoot CompletedModel()
    {
        var model = Model();
        model.Interfaces!.Add(new CkInterfaceDto
        {
            InterfaceId = "Labeled-1",
            Extends = [$"{M}/Named-1"],
            Attributes = [new() { CkAttributeId = $"{M}/Serial", AttributeName = "Serial", IsOptional = true }],
            Associations =
            [
                new()
                {
                    CkRoleId = "System/ParentChild", TargetCkInterfaceId = $"{M}/Named-1",
                    Multiplicity = MultiplicitiesDto.ZeroOrOne, IsOptional = true
                }
            ],
            Methods = [new() { MethodId = "Relabel-1", Visibility = CkVisibilityDto.Internal }]
        });
        model.Interfaces!.Add(new CkInterfaceDto
        {
            InterfaceId = "Tagged-1", Extends = [$"{M}/Labeled-1"], Deprecated = true,
            Methods = [new() { MethodId = "Tag-1", Kind = CkMethodKindDto.Static }]
        });
        return model;
    }

    [Fact]
    public void Extends_IsTransitive_AndMembersAreInherited()
    {
        var operationResult = new OperationResult();
        var graph = Resolve(CompletedModel(), operationResult);
        Assert.Empty(operationResult.Messages);

        var tagged = graph.Interfaces[$"{M}/Tagged-1"];
        Assert.Equal([$"{M}/Labeled-1"], tagged.DeclaredExtends.Select(i => i.ToRtCkId().FullName));
        Assert.Equal(["Labeled-1", "Named-1"], tagged.AllExtendedInterfaces.Select(i => i.ElementId.FullName));
        Assert.True(tagged.Deprecated);
        Assert.Empty(tagged.Attributes);
        Assert.Equal(["Description", "Name", "Serial"], tagged.AllAttributes.Values.Select(a => a.AttributeName).OrderBy(n => n));
        var association = Assert.Single(tagged.AllAssociations);
        Assert.Equal($"{M}/Labeled-1", association.DeclaringCkInterfaceId.ToRtCkId().FullName);
        Assert.Equal(MultiplicitiesDto.ZeroOrOne, association.Definition.Multiplicity);
        Assert.Equal(["Relabel-1", "Tag-1"], tagged.AllMethods.Keys.OrderBy(k => k));
        Assert.Equal($"{M}/Labeled-1", tagged.AllMethods["Relabel-1"].DeclaringCkInterfaceId.ToRtCkId().FullName);
        Assert.Equal(CkVisibilityDto.Internal, tagged.AllMethods["Relabel-1"].Visibility);
        Assert.False(graph.Interfaces[$"{M}/Named-1"].Deprecated);
    }

    [Fact]
    public void ImplementingAnExtendingInterface_ImplementsTheExtendedOnes()
    {
        var model = CompletedModel();
        Type(model, "Tag").Implements = [$"{M}/Tagged-1"];
        Type(model, "Tag").Attributes!.Add(new CkTypeAttributeDto { CkAttributeId = $"{M}/Serial", AttributeName = "Serial", IsOptional = true });

        var operationResult = new OperationResult();
        var graph = Resolve(model, operationResult);
        Assert.Empty(operationResult.Messages);

        var tag = graph.Types[$"{M}/Tag"];
        Assert.Equal(["Labeled-1", "Named-1", "Tagged-1"],
            tag.AllImplementedInterfaces.Select(i => i.ElementId.FullName).OrderBy(n => n));
        Assert.Contains(graph.Interfaces[$"{M}/Named-1"].ImplementingTypes, t => t == tag.CkTypeId);
        Assert.Contains(graph.Interfaces[$"{M}/Labeled-1"].ImplementingTypes, t => t == tag.CkTypeId);
    }

    [Fact]
    public void InheritedRequiredMember_MissingOnTheType_Is96()
    {
        var model = CompletedModel();
        // Strict-1 declares no member of its own; the required Serial member comes from Serialized-1.
        model.Interfaces!.Add(new CkInterfaceDto { InterfaceId = "Strict-1", Extends = [$"{M}/Serialized-1"] });
        model.Types!.Add(new CkCompiledTypeDto
        {
            TypeId = "Plain", DerivedFromCkTypeId = "System/Entity", Implements = [$"{M}/Strict-1"]
        });

        var message = Assert.Single(ResolveExpectingOnly(model, 96));
        Assert.Contains("Serial", message.MessageText);
    }

    [Fact]
    public void ExtendsCycle_DoesNotHang()
    {
        var model = Model();
        model.Interfaces![0].Extends = [$"{M}/Serialized-1"];
        model.Interfaces![1].Extends = [$"{M}/Named-1"];

        var graph = Resolve(model, new OperationResult());

        Assert.Equal(["Serialized-1"], graph.Interfaces[$"{M}/Named-1"].AllExtendedInterfaces.Select(i => i.ElementId.FullName));
        Assert.Equal(["Named-1"], graph.Interfaces[$"{M}/Serialized-1"].AllExtendedInterfaces.Select(i => i.ElementId.FullName));
    }

    [Fact]
    public void TypeAssociation_TargetCkInterfaceId_IsOnTheGraph()
    {
        var model = CompletedModel();
        Type(model, "Tag").Associations =
            [new() { CkRoleId = "System/ParentChild", TargetCkTypeId = "System/Entity", TargetCkInterfaceId = $"{M}/Named-1" }];

        var operationResult = new OperationResult();
        var graph = Resolve(model, operationResult);
        Assert.Empty(operationResult.Messages);

        var association = graph.Types[$"{M}/Tag"].Associations.Out.Owned.Single(a => a.TargetCkInterfaceId != null);
        Assert.Equal($"{M}/Named-1", association.TargetCkInterfaceId!.ToRtCkId().FullName);
        Assert.Null(graph.Types[$"{M}/Tag"].Associations.In.Owned.FirstOrDefault()?.TargetCkInterfaceId);
    }

    [Fact]
    public void Code90_TargetCkInterfaceIdInV1Model()
    {
        var model = sampleData.sample1.Builder.Build();
        model.Types!.Single(t => t.TypeId == "Demo3").Associations![0].TargetCkInterfaceId = "sample1/Named-1";

        var message = Assert.Single(ResolveExpectingOnly(model, 90));
        Assert.Contains("'targetCkInterfaceId'", message.MessageText);
    }

    [Fact]
    public void ReferenceRewriter_CoversTheNewReferences()
    {
        var model = CompletedModel();
        Type(model, "Tag").Associations =
            [new() { CkRoleId = "System/ParentChild", TargetCkTypeId = "System/Entity", TargetCkInterfaceId = "System/Named-1" }];
        model.Interfaces!.Single(i => i.InterfaceId.FullName == "Labeled-1").Associations!.Add(new CkInterfaceAssociationDto
        {
            CkRoleId = "System/ParentChild", TargetCkTypeId = "System/Entity"
        });
        model.Interfaces!.Single(i => i.InterfaceId.FullName == "Tagged-1").Methods![0].Parameters =
            [new() { Name = "mode", ValueType = AttributeValueTypesDto.Enum, ValueCkEnumId = "System/Mode" }];

        var references = CkReferenceRewriter.CollectReferences(model);

        Assert.Contains(references, r => r is { Kind: "interface", ElementId: "Named-1" } && r.ModelId.Name == "System");
        Assert.Contains(references, r => r is { Kind: "interface", ElementId: "Labeled-1" } && r.ModelId.Name == M);
        Assert.Contains(references, r => r.Kind == "type" && r.ElementId.StartsWith("Entity") && r.ModelId.Name == "System");
        Assert.Contains(references, r => r.Kind == "enum" && r.ElementId.StartsWith("Mode") && r.ModelId.Name == "System");

        CkReferenceRewriter.Rewrite(model, id => id.Name == "System" ? CkModelId.MajorQualified("System", 2) : null);

        Assert.True(Type(model, "Tag").Associations![0].TargetCkInterfaceId!.ModelId.IsMajorQualified);
        var labeled = model.Interfaces!.Single(i => i.InterfaceId.FullName == "Labeled-1");
        Assert.True(labeled.Associations![0].CkRoleId.ModelId.IsMajorQualified);
        Assert.True(labeled.Associations![1].TargetCkTypeId!.ModelId.IsMajorQualified);
        Assert.False(labeled.Extends![0].ModelId.IsMajorQualified);
        Assert.True(model.Interfaces!.Single(i => i.InterfaceId.FullName == "Tagged-1").Methods![0].Parameters![0]
            .ValueCkEnumId!.ModelId.IsMajorQualified);
    }
}
