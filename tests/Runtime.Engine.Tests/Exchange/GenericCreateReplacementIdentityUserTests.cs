using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Exchange;

/// <summary>
///     AB#7777: <see cref="GenericCreateReplacement" /> treats the static <c>User.CreateUser-1</c> of System.Identity as the
///     method that replaces the generic create of <c>System.Identity/User</c>, although System.Identity 2.x cannot carry the
///     <c>replacesGenericCreate</c> flag (a Major, row M16). Every generic writer asks this helper, so a new user is refused on
///     every generic path (GraphQL, MCP, mesh-adapter insert, caller ImportRt, blueprint seeds). The tests use self-made type
///     graphs modelled on System.Identity; they depend on no System.Identity version.
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

    private static CkTypeGraph IdentityUser(string model = "System.Identity-2.30.0") => Graph($"{model}/User",
        Method($"{model}/User", "ChangePassword-1", CkMethodKindDto.Instance),
        Method($"{model}/User", GenericCreateReplacement.IdentityUserCreateMethodId, CkMethodKindDto.Static));

    [Theory]
    [InlineData("System.Identity-2.30.0")]
    [InlineData("System.Identity-2.29.0")]
    [InlineData("System.Identity-3.1.0")]
    public void TheIdentityUser_IsReplacedByUserCreateUser(string model)
    {
        var method = GenericCreateReplacement.FindReplacingMethod(IdentityUser(model));

        Assert.NotNull(method);
        Assert.Equal("System.Identity/User.CreateUser-1", method.QualifiedMethodId);
        Assert.False(method.ReplacesGenericCreate, "the CK meta keeps reporting the model as it is");
    }

    [Fact]
    public void AnIdentityModelWithoutTheMethod_KeepsTheGenericCreate()
    {
        // System.Identity below 2.29 declares no CreateUser: there is no method to point the caller to.
        var user = Graph("System.Identity-2.28.0/User",
            Method("System.Identity-2.28.0/User", "ChangePassword-1", CkMethodKindDto.Instance));

        Assert.Null(GenericCreateReplacement.FindReplacingMethod(user));
    }

    [Fact]
    public void AnInstanceMethodOfTheSameName_DoesNotReplaceTheCreate()
    {
        var user = Graph("System.Identity-2.30.0/User",
            Method("System.Identity-2.30.0/User", GenericCreateReplacement.IdentityUserCreateMethodId,
                CkMethodKindDto.Instance));

        Assert.Null(GenericCreateReplacement.FindReplacingMethod(user));
    }

    [Theory]
    [InlineData("Test-1.0.0/User")]
    [InlineData("System.Identity-2.30.0/Group")]
    [InlineData("System.Identity.Extra-1.0.0/User")]
    public void AnotherType_WithAStaticCreateUser_IsNotReplaced(string ckTypeId)
    {
        var other = Graph(ckTypeId,
            Method(ckTypeId, GenericCreateReplacement.IdentityUserCreateMethodId, CkMethodKindDto.Static));

        Assert.Null(GenericCreateReplacement.FindReplacingMethod(other));
    }

    [Fact]
    public void AFlaggedMethodOfTheModel_WinsOverTheRuntimeRule()
    {
        // The next System.Identity major sets the flag in the model (AB#8053): the model's method is the answer.
        var user = Graph("System.Identity-3.0.0/User",
            Method("System.Identity-3.0.0/User", GenericCreateReplacement.IdentityUserCreateMethodId,
                CkMethodKindDto.Static),
            Method("System.Identity-3.0.0/User", "Create-2", CkMethodKindDto.Static, replacesGenericCreate: true));

        var method = GenericCreateReplacement.FindReplacingMethod(user);

        Assert.NotNull(method);
        Assert.Equal("System.Identity/User.Create-2", method.QualifiedMethodId);
        Assert.True(method.ReplacesGenericCreate);
    }

    [Fact]
    public void TryGetReplacingMethod_FromTheCkCache_AnswersTheSameForAVersionLessRuntimeId()
    {
        var cache = A.Fake<ICkCacheService>();
        var graph = (CkTypeGraph?)IdentityUser();
        var rtId = new RtCkId<CkTypeId>("System.Identity/User");
        A.CallTo(() => cache.TryGetRtCkType(Tenant, rtId, out graph)).Returns(true)
            .AssignsOutAndRefParameters(graph);

        Assert.True(GenericCreateReplacement.TryGetReplacingMethod(cache, Tenant, rtId, out var method));
        Assert.Equal("System.Identity/User.CreateUser-1", method!.QualifiedMethodId);
    }
}
