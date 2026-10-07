using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.SourceGeneration;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SourceGeneration;

/// <summary>
///     CK v2 Phase 0 (AB#5667 / AB#5669): the generated <c>*CkIds</c> class carries interface id and qualified
///     method id constants, e.g. <c>SystemIdentityCkIds.UserChangePasswordMethodId</c>.
/// </summary>
public class CkIdsCodeGeneratorCkV2Tests
{
    private static string Generate(List<CkInterfaceDto>? interfaces, List<CkCompiledTypeDto> types) =>
        CkIdsCodeGenerator.Instance.Generate("Test.Generated", new CkModelId("System.Identity", "2.90.0"), types,
            null, null, null, null, interfaces);

    [Fact]
    public void Generate_EmitsInterfaceAndMethodConstants()
    {
        var code = Generate(
            [
                new() { InterfaceId = "Named-1", Attributes = [] },
                new() { InterfaceId = "Named-2", Attributes = [] }
            ],
            [
                new()
                {
                    TypeId = "User",
                    Methods = [new() { MethodId = "ChangePassword-1" }, new() { MethodId = "ChangePassword-2" }]
                },
                new() { TypeId = "Role" }
            ]);

        Assert.Contains("public static readonly RtCkId<CkInterfaceId> RtCkNamedInterfaceId = new RtCkId<CkInterfaceId>(ModelIdName, \"Named-1\");", code);
        Assert.Contains("public static readonly CkId<CkInterfaceId> CkNamedInterfaceId = new CkId<CkInterfaceId>(CkModelId, \"Named-1\");", code);
        Assert.Contains("public const string RtCkNamedInterfaceIdString = \"System.Identity/Named-1\";", code);
        Assert.Contains("public const string RtCkNamed2InterfaceIdString = \"System.Identity/Named-2\";", code);
        Assert.Contains("public const string UserChangePasswordMethodId = \"System.Identity/User.ChangePassword-1\";", code);
        Assert.Contains("public const string UserChangePassword2MethodId = \"System.Identity/User.ChangePassword-2\";", code);
    }

    [Fact]
    public void Generate_WithoutCkV2Elements_EmitsNoCkV2Sections()
    {
        var code = Generate(null, [new() { TypeId = "Role" }]);

        Assert.DoesNotContain("// Interfaces", code);
        Assert.DoesNotContain("// Methods", code);
    }
}
