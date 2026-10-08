using System.Text;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Tests.CkV2;
using Meshmakers.Octo.ConstructionKit.Engine.Tests.Resolvers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.DependencyGraph;

/// <summary>
///     CK v2 Phase 0 engine companion of the persistence round trip (contract 3.4): a resolved CK v2 graph survives
///     <c>ToCkCacheRoot</c> → cache JSON → <c>CkModelGraph(CkCacheRoot)</c> with every new graph property. Guards
///     the <c>[JsonConstructor]</c> of <c>CkTypeGraph</c> / <c>CkInterfaceGraph</c> and the init setter of
///     <c>CkTypeAttributeGraph.Access</c> (the isRuntimeState class of bug).
/// </summary>
public class CkV2GraphJsonRoundTripTests(ITestOutputHelper output) : CkV2ResolverTestBase(output)
{
    private static async Task<CkCacheService> RoundTripAsync(Engine.DependencyGraph.CkModelGraph graph)
    {
        var source = new CkCacheService(NullLogger<CkCacheService>.Instance);
        source.CreateTenant("source");
        source.LoadCkModelGraph("source", graph);
        using var stream = new MemoryStream();
        await source.SaveCacheAsync("source", stream);

        var target = new CkCacheService(NullLogger<CkCacheService>.Instance);
        target.CreateTenant("target");
        target.RestoreCache("target", Encoding.UTF8.GetString(stream.ToArray()));
        return target;
    }

    [Fact]
    public async Task ResolvedKitchenSinkGraph_SurvivesTheCacheJson()
    {
        var operationResult = new OperationResult();
        var graph = Resolve(Model(), operationResult);
        Assert.Empty(operationResult.Messages);

        var cache = await RoundTripAsync(graph);

        // Interfaces
        var named = cache.GetRtCkInterface("target", new RtCkId<CkInterfaceId>($"{M}/Named-1"));
        var original = graph.Interfaces[$"{M}/Named-1"];
        Assert.Equal(original.CkInterfaceId, named.CkInterfaceId);
        Assert.Equal(original.Description, named.Description);
        Assert.Equal(original.Attributes.Keys.OrderBy(k => k), named.Attributes.Keys.OrderBy(k => k));
        Assert.Equal(original.Attributes.Values.Select(a => (a.AttributeName, a.IsOptional, a.ValueType)).OrderBy(a => a.AttributeName),
            named.Attributes.Values.Select(a => (a.AttributeName, a.IsOptional, a.ValueType)).OrderBy(a => a.AttributeName));
        Assert.Equal(original.ImplementingTypes.OrderBy(t => t), named.ImplementingTypes.OrderBy(t => t));
        Assert.Equal(2, cache.GetRtCkInterfaces("target").Count);

        // Types: implements, methods, access
        var account = cache.GetRtCkType("target", new RtCkId<CkTypeId>($"{M}/Account"));
        var originalAccount = graph.Types[$"{M}/Account"];
        Assert.Equal(originalAccount.DeclaredImplements, account.DeclaredImplements);
        Assert.Equal(originalAccount.AllImplementedInterfaces.OrderBy(i => i), account.AllImplementedInterfaces.OrderBy(i => i));
        Assert.Equal(originalAccount.DefinedMethods.Select(m => m.MethodId), account.DefinedMethods.Select(m => m.MethodId));
        Assert.Equal(originalAccount.AllMethods.Keys.OrderBy(k => k), account.AllMethods.Keys.OrderBy(k => k));
        var changePassword = account.AllMethods["ChangePassword-1"];
        Assert.Equal(originalAccount.AllMethods["ChangePassword-1"].DeclaringCkTypeId, changePassword.DeclaringCkTypeId);
        CkV2Assert.MethodsEqual(sampleData.ckv2KitchenSink.Builder.FullMethod(), changePassword.Definition);
        Assert.Equal(CkMethodKindDto.Static, account.AllMethods["Lock-1"].Definition.Kind);

        foreach (var (name, access) in new[]
                 {
                     ("Name", CkAttributeAccessDto.ReadWrite), ("Status", CkAttributeAccessDto.MethodOnly),
                     ("Serial", CkAttributeAccessDto.ReadOnly), ("PasswordHash", CkAttributeAccessDto.Hidden)
                 })
        {
            Assert.Equal(access, account.AllAttributesByName[name].Access);
        }

        var address = cache.GetRtCkRecord("target", new RtCkId<CkRecordId>($"{M}/Address"));
        Assert.Equal(CkAttributeAccessDto.Hidden, address.AllAttributesByName["City"].Access);

        // Model properties carry ckLanguage.
        var cacheRoot = graph.ToCkCacheRoot();
        Assert.Equal(2, cacheRoot.Models.Single(m => m.ModelId.Name == M).CkLanguage);
    }

    [Fact]
    public async Task V1Graph_CacheJsonHasNoCkV2Keys_AndRestoresDefaults()
    {
        var operationResult = new OperationResult();
        var graph = Resolve(sampleData.sample1.Builder.Build(), operationResult);
        Assert.Empty(operationResult.Messages);

        var source = new CkCacheService(NullLogger<CkCacheService>.Instance);
        source.CreateTenant("source");
        source.LoadCkModelGraph("source", graph);
        using var stream = new MemoryStream();
        await source.SaveCacheAsync("source", stream);
        var json = Encoding.UTF8.GetString(stream.ToArray());

        foreach (var key in new[] { "\"interfaces\"", "\"declaredImplements\"", "\"allImplementedInterfaces\"",
                     "\"definedMethods\"", "\"allMethods\"", "\"access\"", "\"ckLanguage\"", "\"visibility\"", "\"derivable\"" })
        {
            Assert.DoesNotContain(key, json);
        }

        var cache = await RoundTripAsync(graph);
        var demo3 = cache.GetRtCkType("target", new RtCkId<CkTypeId>("sample1/Demo3"));
        Assert.Empty(demo3.AllImplementedInterfaces);
        Assert.Empty(demo3.AllMethods);
        Assert.All(demo3.AllAttributes.Values, a => Assert.Equal(CkAttributeAccessDto.ReadWrite, a.Access));
        Assert.Empty(cache.GetRtCkInterfaces("target"));
        Assert.Equal(CkVisibilityDto.Public, demo3.Visibility);
        Assert.Equal(CkDerivableDto.Any, demo3.Derivable);
    }

    [Fact]
    public async Task V2Modifiers_SurviveTheCacheJson()
    {
        // F1.1-S4 (AB#5907): effective visibility / derivable, including the v2 derivable default (Model).
        var model = Model();
        Type(model, "Tag").Visibility = CkVisibilityDto.Internal;
        Type(model, "Tag").Derivable = CkDerivableDto.Any;
        model.Records!.Single().Visibility = CkVisibilityDto.Internal;
        model.Enums!.Single().Visibility = CkVisibilityDto.Internal;
        model.Attributes!.Single(a => a.AttributeId == "PasswordHash").Visibility = CkVisibilityDto.Internal;
        model.Interfaces!.Single(i => i.InterfaceId == "Named-1").Visibility = CkVisibilityDto.Internal;
        var method = Type(model, "Account").Methods![0];
        method.Visibility = CkVisibilityDto.Internal;
        var operationResult = new OperationResult();
        var graph = Resolve(model, operationResult);
        Assert.Empty(operationResult.Messages);

        var cache = await RoundTripAsync(graph);

        var tag = cache.GetRtCkType("target", new RtCkId<CkTypeId>($"{M}/Tag"));
        Assert.Equal(CkVisibilityDto.Internal, tag.Visibility);
        Assert.Equal(CkDerivableDto.Any, tag.Derivable);
        var account = cache.GetRtCkType("target", new RtCkId<CkTypeId>($"{M}/Account"));
        Assert.Equal(CkVisibilityDto.Public, account.Visibility);
        Assert.Equal(CkDerivableDto.Model, account.Derivable);
        Assert.Equal(CkVisibilityDto.Internal, account.AllMethods[method.MethodId.ToString()].Visibility);
        var address = cache.GetRtCkRecord("target", new RtCkId<CkRecordId>($"{M}/Address"));
        Assert.Equal(CkVisibilityDto.Internal, address.Visibility);
        Assert.Equal(CkDerivableDto.Model, address.Derivable);
        Assert.Equal(CkVisibilityDto.Internal, cache.GetRtCkEnum("target", new RtCkId<CkEnumId>($"{M}/Mode")).Visibility);
        Assert.Equal(CkVisibilityDto.Internal,
            cache.GetRtCkAttribute("target", new RtCkId<CkAttributeId>($"{M}/PasswordHash")).Visibility);
        Assert.Equal(CkVisibilityDto.Public,
            cache.GetRtCkAttribute("target", new RtCkId<CkAttributeId>($"{M}/Name")).Visibility);
        Assert.Equal(CkVisibilityDto.Internal,
            cache.GetRtCkInterface("target", new RtCkId<CkInterfaceId>($"{M}/Named-1")).Visibility);
    }

    [Fact]
    public async Task V2AssociationRoleVisibility_SurvivesTheCacheJson()
    {
        var model = sampleData.sample1.Builder.Build();
        model.CkLanguage = 2;
        model.AssociationRoles!.Single().Visibility = CkVisibilityDto.Internal;
        var operationResult = new OperationResult();
        var graph = Resolve(model, operationResult);
        Assert.Empty(operationResult.Messages);

        var cache = await RoundTripAsync(graph);

        Assert.Equal(CkVisibilityDto.Internal,
            cache.GetRtCkAssociationRole("target", new RtCkId<CkAssociationRoleId>("sample1/Related")).Visibility);
    }
}
