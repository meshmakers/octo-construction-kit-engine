using System.Text.Json;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Secrets;
using Meshmakers.Octo.Runtime.Engine.Repositories.Query;
using Meshmakers.Octo.Runtime.Engine.Secrets;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Secrets;

/// <summary>
///     AB#5532 (handover §7, Q1): the secrets overview over a faked repository - forms, record paths,
///     re-entry rule, filters, paging, summary, and that nothing carries a value.
/// </summary>
public class SecretInventoryServiceTests
{
    private const string UnknownKidEnvelope = "enc:v2:k9:AAECAwQFBgcICQoLce30XInwk1La5ADQECWjFW2r_6nUXsA";
    private static readonly DateTime Earlier = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private readonly SecretTestModel _model = new();
    private readonly SecretAttributeProtector _protector = SecretTestModel.CreateProtector();
    private readonly IRuntimeRepository _repository = A.Fake<IRuntimeRepository>();
    private readonly IRuntimeRepositoryProvider _provider = A.Fake<IRuntimeRepositoryProvider>();
    private readonly Dictionary<string, List<RtEntity>> _store = new(StringComparer.Ordinal);
    private readonly RtEntity _config, _optional;

    public SecretInventoryServiceTests()
    {
        _config = _model.NewConfig();
        _config.RtWellKnownName = "mailbox";
        _config.RtDisplayName = "Office mailbox";
        _config.SetAttributeRawValue("Password", "legacy-clear");
        _config.SetAttributeRawValue("ApiKey", RtSecretValue.Protected(UnknownKidEnvelope, Earlier));
        _config.SetAttributeRawValue("Credentials", new List<RtRecord>
        {
            _model.CredentialRecord("prod", _protector.Protect("p").WithSetAt(Earlier)),
            _model.CredentialRecord("test", null)
        });
        _config.SetAttributeRawValue("Primary", _model.CredentialRecord("main", SecretTestModel.V1Vector));

        _optional = _model.NewOptionalOnly();
        _optional.SetAttributeRawValue("Password", RtSecretValue.LegacyPlaintext(_protector.Protect("copied").Envelope!));

        _store[_model.Config.CkTypeId.ToRtCkId().FullName] = [_config];
        _store[_model.OptionalOnly.CkTypeId.ToRtCkId().FullName] = [_optional];

        A.CallTo(() => _provider.GetRepositoryAsync(SecretTestModel.TenantId, A<CancellationToken>._))
            .Returns(_repository);
        A.CallTo(() => _repository.GetRtEntitiesByTypeAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                A<RtEntityQueryOptions>._, A<int?>._, A<int?>._))
            .ReturnsLazily(call =>
            {
                var typeId = call.GetArgument<RtCkId<CkTypeId>>(1)!;
                var skip = call.GetArgument<int?>(3) ?? 0;
                var take = call.GetArgument<int?>(4) ?? int.MaxValue;
                var all = _store.TryGetValue(typeId.FullName, out var list) ? list : [];
                IResultSet<RtEntity> page = new ResultSet<RtEntity>(all.Skip(skip).Take(take).ToList(), all.Count,
                    null, null);
                return Task.FromResult(page);
            });
    }

    private SecretInventoryService CreateService(SecretAttributeProtector? protector = null)
    {
        protector ??= _protector;
        return new SecretInventoryService(_provider, _model.Cache, protector, new SecretWriteNormalizer(protector));
    }

    private Task<SecretInventoryPage> ListAsync(SecretInventoryQuery? query = null) =>
        CreateService().ListAsync(SecretTestModel.TenantId, query ?? new SecretInventoryQuery { Take = 100 },
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task List_ReportsEverySlot_WithFormsPathsAndReEntry()
    {
        var page = await ListAsync();
        var configType = _model.Config.CkTypeId.ToRtCkId().ToString();

        Assert.Equal(6, page.TotalCount);
        var byPath = page.Items.Where(i => i.RtId == _config.RtId).ToDictionary(i => i.AttributePath);
        Assert.Equal(["password", "apiKey", "credentials[key=prod].value", "credentials[key=test].value", "primary.value"],
            byPath.Keys);

        var password = byPath["password"];
        Assert.Equal((SecretStorageForm.Plaintext, false, false, "Password"),
            (password.Form, password.Required, password.NeedsReEntry, password.AttributeName));
        Assert.Equal(configType, password.CkTypeId);
        Assert.Equal("mailbox", password.RtWellKnownName);
        Assert.Equal("Office mailbox", password.DisplayName);

        var apiKey = byPath["apiKey"];
        Assert.Equal((SecretStorageForm.KeyMissing, "k9", Earlier, true, true),
            (apiKey.Form, apiKey.KeyId, apiKey.SetAt, apiKey.Required, apiKey.NeedsReEntry));

        var prod = byPath["credentials[key=prod].value"];
        Assert.Equal((SecretStorageForm.EncV2, "k1", Earlier, false, "Credentials"),
            (prod.Form, prod.KeyId, prod.SetAt, prod.NeedsReEntry, prod.AttributeName));
        Assert.Equal(SecretStorageForm.NotSet, byPath["credentials[key=test].value"].Form);
        Assert.False(byPath["credentials[key=test].value"].NeedsReEntry); // optional
        Assert.Equal(SecretStorageForm.EncV1, byPath["primary.value"].Form);
        Assert.Null(byPath["primary.value"].SetAt);

        var corrupt = Assert.Single(page.Items, i => i.RtId == _optional.RtId && i.AttributePath == "password");
        Assert.Equal(SecretStorageForm.Corrupt, corrupt.Form);
        Assert.True(corrupt.NeedsReEntry);
        Assert.Null(corrupt.KeyId);

        // RequiredCredentials / Wrappers are null: no slots.
        Assert.DoesNotContain(page.Items, i => i.AttributeName is "RequiredCredentials" or "Wrappers");
        AssertNoValues(page);
    }

    [Fact]
    public async Task List_RequiredRecordMemberNotSet_NeedsReEntry_AndIndexPathWithoutKey()
    {
        _config.SetAttributeRawValue("RequiredCredentials", new List<RtRecord>
        {
            new() { CkRecordId = _model.RequiredCredential.CkRecordId.ToRtCkId() }
        });

        var page = await ListAsync(new SecretInventoryQuery { NeedsReEntry = true, Take = 100 });

        // No key value on the element: addressed by index.
        var item = Assert.Single(page.Items, i => i.AttributePath == "requiredCredentials[0].secret");
        Assert.True(item.Required);
        Assert.Equal(SecretStorageForm.NotSet, item.Form);
        Assert.Equal(3, page.TotalCount); // apiKey (key missing), corrupt, required member
    }

    [Fact]
    public async Task List_Filters_FormsCkTypeAndSearch_AndPages()
    {
        var forms = await ListAsync(new SecretInventoryQuery
        {
            Forms = [SecretStorageForm.EncV2, SecretStorageForm.EncV1], Take = 100
        });
        Assert.Equal(2, forms.TotalCount);

        var optionalOnly = await ListAsync(new SecretInventoryQuery
        {
            CkTypeId = _model.OptionalOnly.CkTypeId.ToRtCkId().FullName, Take = 100
        });
        Assert.All(optionalOnly.Items, i => Assert.Equal(_optional.RtId, i.RtId));
        Assert.Equal(1, optionalOnly.TotalCount);

        var unknownType = await ListAsync(new SecretInventoryQuery { CkTypeId = "Nope/Missing", Take = 100 });
        Assert.Equal(0, unknownType.TotalCount);

        Assert.Equal(5, (await ListAsync(new SecretInventoryQuery { Search = "MAILBOX", Take = 100 })).TotalCount);
        Assert.Equal(2, (await ListAsync(new SecretInventoryQuery { Search = "credentials[", Take = 100 })).TotalCount);
        Assert.Equal(1, (await ListAsync(new SecretInventoryQuery { Search = _optional.RtId.ToString(), Take = 100 })).TotalCount);
        // Values are never matched.
        Assert.Equal(0, (await ListAsync(new SecretInventoryQuery { Search = "legacy-clear", Take = 100 })).TotalCount);

        var all = await ListAsync();
        var page = await ListAsync(new SecretInventoryQuery { Skip = 2, Take = 3 });
        Assert.Equal(6, page.TotalCount);
        Assert.Equal(all.Items.Skip(2).Take(3).Select(i => i.AttributePath + i.RtId),
            page.Items.Select(i => i.AttributePath + i.RtId));
    }

    [Fact]
    public async Task Summarize_CountsPerForm()
    {
        var summary = await CreateService().SummarizeAsync(SecretTestModel.TenantId,
            TestContext.Current.CancellationToken);

        Assert.Equal(6, summary.Total);
        Assert.Equal(1, summary.NotSet);
        Assert.Equal(1, summary.Plaintext);
        Assert.Equal(1, summary.EncV1);
        Assert.Equal(1, summary.EncV2);
        Assert.Equal(1, summary.KeyMissing);
        Assert.Equal(1, summary.Corrupt);
        Assert.Equal(2, summary.NeedsReEntry);
        Assert.Equal(1, summary.EncV2ByKeyId["k1"]);
        Assert.Equal(summary.Total, summary.NotSet + summary.Plaintext + summary.EncV1 + summary.EncV2 +
                                    summary.KeyMissing + summary.Corrupt);
    }

    [Fact]
    public async Task WithoutKeys_ProtectedValuesAreKeyMissing()
    {
        var page = await CreateService(SecretTestModel.CreateProtector(configured: false))
            .ListAsync(SecretTestModel.TenantId, new SecretInventoryQuery { Take = 100 },
                TestContext.Current.CancellationToken);

        Assert.Equal(SecretStorageForm.KeyMissing,
            Assert.Single(page.Items, i => i.AttributePath == "credentials[key=prod].value").Form);
    }

    [Fact]
    public void AddRuntimeEngine_RegistersTheInventoryService()
    {
        var services = new ServiceCollection();
        services.AddRuntimeEngine();

        Assert.Contains(services, d => d.ServiceType == typeof(ISecretInventoryService) &&
                                       d.ImplementationType == typeof(SecretInventoryService));
    }

    [Theory]
    [InlineData("Password", "password")]
    [InlineData("apiKey", "apiKey")]
    [InlineData("", "")]
    public void ToCamelCase(string input, string expected)
    {
        Assert.Equal(expected, SecretInventoryService.ToCamelCase(input));
    }

    private static void AssertNoValues(SecretInventoryPage page)
    {
        var json = JsonSerializer.Serialize(page);
        Assert.DoesNotContain("legacy-clear", json);
        Assert.DoesNotContain("enc:v", json);
        Assert.DoesNotContain(SecretTestModel.V1VectorPlaintext, json);
    }
}
