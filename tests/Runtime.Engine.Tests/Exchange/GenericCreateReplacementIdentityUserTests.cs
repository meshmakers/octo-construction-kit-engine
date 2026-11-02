using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Exchange;

/// <summary>
///     AB#7777 / AB#8053: System.Identity 3.0.0 sets <c>replacesGenericCreate</c> on the static <c>User.CreateUser-1</c> in the
///     model, so <see cref="GenericCreateReplacement" /> answers it from the flag like for every other type. The runtime
///     replacement that stood in for the flag in System.Identity 2.x (AB#7777, interim) is gone: an unflagged
///     <c>CreateUser-1</c> - the 2.x shape - no longer replaces the generic create. The tests use self-made type graphs modelled
///     on System.Identity; they depend on no System.Identity version.
/// </summary>
public class GenericCreateReplacementIdentityUserTests
{
    private const string Tenant = "tenant1";

    private static CkTypeGraph Graph(string ckTypeId, params CkMethodGraph[] methods) => new(
        new CkId<CkTypeId>(ckTypeId),
        isAbstract: false, isFinal: true, isCollectionRoot: false, baseTypes: [],
        derivedFromCkTypeId: null, definingCollectionRootCkTypeId: null, derivedTypes: [], definedAttributes: [],
        allAttributes: new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>(), indexes: [],
        associations: new CkGraphDirectedAssociations([]), description: "test",
        enableChangeStreamPreAndPostImages: false,
        allMethods: methods.Length == 0 ? null : methods.ToDictionary(m => m.Definition.MethodId, m => m));

    private static CkMethodGraph Method(string ckTypeId, string methodId, CkMethodKindDto kind,
        bool replacesGenericCreate = false) => new(new CkId<CkTypeId>(ckTypeId),
        new CkMethodDto { MethodId = methodId, Kind = kind, ReplacesGenericCreate = replacesGenericCreate });

    private static CkTypeGraph IdentityUser(string model, bool flagged) => Graph($"{model}/User",
        Method($"{model}/User", "ChangePassword-1", CkMethodKindDto.Instance),
        Method($"{model}/User", "CreateUser-1", CkMethodKindDto.Static, flagged));

    [Theory]
    [InlineData("System.Identity-3.0.0")]
    [InlineData("System.Identity-3.1.0")]
    public void TheIdentityUser_WithTheModelFlag_IsReplacedByUserCreateUser(string model)
    {
        var method = GenericCreateReplacement.FindReplacingMethod(IdentityUser(model, flagged: true));

        Assert.NotNull(method);
        Assert.Equal("System.Identity/User.CreateUser-1", method.QualifiedMethodId);
        Assert.True(method.ReplacesGenericCreate);
    }

    [Theory]
    [InlineData("System.Identity-2.30.0")]
    [InlineData("System.Identity-2.29.0")]
    public void TheIdentityUser_WithoutTheFlag_KeepsTheGenericCreate_ThereIsNoRuntimeRuleAnyMore(string model)
    {
        // The 2.x shape: CreateUser-1 declared, not flagged. The interim runtime rule of AB#7777 is removed with AB#8053.
        Assert.Null(GenericCreateReplacement.FindReplacingMethod(IdentityUser(model, flagged: false)));
    }

    [Fact]
    public void TryGetReplacingMethod_FromTheCkCache_AnswersTheSameForAVersionLessRuntimeId()
    {
        var cache = A.Fake<ICkCacheService>();
        var graph = (CkTypeGraph?)IdentityUser("System.Identity-3.0.0", flagged: true);
        var rtId = new RtCkId<CkTypeId>("System.Identity/User");
        A.CallTo(() => cache.TryGetRtCkType(Tenant, rtId, out graph)).Returns(true)
            .AssignsOutAndRefParameters(graph);

        Assert.True(GenericCreateReplacement.TryGetReplacingMethod(cache, Tenant, rtId, out var method));
        Assert.Equal("System.Identity/User.CreateUser-1", method!.QualifiedMethodId);
    }
}
