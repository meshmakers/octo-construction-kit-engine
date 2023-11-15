using IdentityModel;
using Meshmakers.Common.Shared;
using Meshmakers.Octo.Common.Shared;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.SystematizedData.Persistence.DataAccess;
using Meshmakers.Octo.SystematizedData.Persistence.SystemTests.Fixtures;
using TestCkModel.ConstructionKit.Generated.System.TestIdentity.v1;
using Xunit;

namespace Meshmakers.Octo.SystematizedData.Persistence.SystemTests;

public class NewTests : IClassFixture<SystemFixture>
{
    private readonly SystemFixture _systemFixture;

    public NewTests(SystemFixture systemFixture)
    {
        _systemFixture = systemFixture;
    }

    [Fact]
    public async void TestCk()
    {
        var systemContext = _systemFixture.GetSystemContext();

        using var systemSession = await systemContext.GetSystemSessionAsync();
        systemSession.StartTransaction();

        var operationResult = new OperationResult();
        await systemContext.ImportCkModelAsync(systemSession, new CkModelId("System.TestIdentity-1.0.0"), operationResult);

        await systemSession.CommitTransactionAsync();

        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);


        var tenantRepository = await systemContext.GetTenantRepositoryAsync();
        using var userSession = await tenantRepository.GetSessionAsync();
        userSession.StartTransaction();

        await CreateClients(userSession, tenantRepository);
        
        await userSession.CommitTransactionAsync();
        
        
        
    }

    private async Task CreateClients(IOctoSession session, ITenantRepository tenantRepository)
    {
        var appClient = new RtClient
        {
            Enabled = true,
            ClientId = CommonConstants.OctoToolClientId,

            // no interactive user, use the clientId/secret for authentication
            AllowedGrantTypes = new AttributeValueArray<string> { OidcConstants.GrantTypes.DeviceCode },

            // secret for authentication
            ClientSecrets = new AttributeRecordValueArray<RtSecretRecord>
            {
                new() { Value = CommonConstants.OctoToolClientSecret }
            },

            AllowOfflineAccess = true,

            // scopes that client has access to
            AllowedScopes =
            {
                JwtClaimTypes.Role,
                CommonConstants.SystemApiFullAccess,
                CommonConstants.IdentityApiFullAccess,
                CommonConstants.BotApiFullAccess
            }
        };

        await tenantRepository.InsertOneRtEntityAsync(session, appClient);

        appClient = new RtClient
        {
            Enabled = true,
            ClientId = CommonConstants.IdentityServicesSwaggerClientId,

            ClientName = "testaaa",
            ClientUri = "https://localhost:5003",

            AllowedGrantTypes = new AttributeValueArray<string> { OidcConstants.GrantTypes.AuthorizationCode },

            RequirePkce = true,
            RequireClientSecret = false,

            AccessTokenType = RtTokenTypeEnum.Jwt,
            AllowAccessTokensViaBrowser = true,
            AlwaysIncludeUserClaimsInIdToken = true,

            RedirectUris =
            {
                "https://localhost:5003".EnsureEndsWith("/swagger/oauth2-redirect.html")
            },

            PostLogoutRedirectUris = { "https://localhost:5003".EnsureEndsWith("/") },
            AllowedCorsOrigins = { "https://localhost:5003".TrimEnd('/') },
            AllowedScopes =
            {
                CommonConstants.Scopes.OpenId,
                CommonConstants.Scopes.Profile,
                CommonConstants.Scopes.Email,
                JwtClaimTypes.Role,
                CommonConstants.IdentityApiFullAccess,
                CommonConstants.IdentityApiReadOnly
            }
        };

        await tenantRepository.InsertOneRtEntityAsync(session, appClient);
    }
}