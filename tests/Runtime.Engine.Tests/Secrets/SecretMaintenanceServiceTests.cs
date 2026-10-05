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
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Secrets;

/// <summary>
///     AB#5532: the secret sweep over a faked repository - counts per form, every mode, unknown key ids,
///     idempotency, failures, and that results never carry a value.
/// </summary>
public class SecretMaintenanceServiceTests
{
    private const string UnknownKidEnvelope = "enc:v2:k9:AAECAwQFBgcICQoLce30XInwk1La5ADQECWjFW2r_6nUXsA";

    private readonly SecretTestModel _model = new();
    private readonly SecretAttributeProtector _protector = SecretTestModel.CreateProtector();
    private readonly IRuntimeRepository _repository = A.Fake<IRuntimeRepository>();
    private readonly IRuntimeRepositoryProvider _provider = A.Fake<IRuntimeRepositoryProvider>();
    private readonly Dictionary<string, List<RtEntity>> _store = new(StringComparer.Ordinal);
    private readonly List<(OctoObjectId RtId, string Attribute)> _rewrites = [];
    private readonly RtEntity _e1, _e2, _e3, _e4, _e5;

    public SecretMaintenanceServiceTests()
    {
        _e1 = _model.NewConfig();
        _e1.SetAttributeRawValue("Password", "plain-1");
        _e1.SetAttributeRawValue("ApiKey", _protector.Protect("api-1"));

        _e2 = _model.NewConfig();
        _e2.SetAttributeRawValue("Password", "TODO_SET_PASSWORD");
        _e2.SetAttributeRawValue("ApiKey", SecretTestModel.V1Vector);

        _e3 = _model.NewConfig();
        _e3.SetAttributeRawValue("Password", ProtectWithK2("pw-3"));
        _e3.SetAttributeRawValue("ApiKey", RtSecretValue.Protected(UnknownKidEnvelope));

        _e4 = _model.NewConfig();
        _e4.SetAttributeRawValue("Password", null);
        _e4.SetAttributeRawValue("ApiKey", RtSecretValue.LegacyPlaintext("plain-4"));
        _e4.SetAttributeRawValue("Credentials", new List<RtRecord>
        {
            _model.CredentialRecord("a", RtSecretValue.Protected(UnknownKidEnvelope)),
            _model.CredentialRecord("b", "rec-plain")
        });

        _e5 = _model.NewOptionalOnly();
        _e5.SetAttributeRawValue("Password", "");

        _store[_model.Config.CkTypeId.ToRtCkId().FullName] = [_e1, _e2, _e3, _e4];
        _store[_model.OptionalOnly.CkTypeId.ToRtCkId().FullName] = [_e5];
        _store[_model.Plain.CkTypeId.ToRtCkId().FullName] = [];

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
                IResultSet<RtEntity> page = new ResultSet<RtEntity>(all.Skip(skip).Take(take), all.Count, null, null);
                return Task.FromResult(page);
            });
        A.CallTo(() => _repository.RewriteAttributeValueForMigrationAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                A<OctoObjectId>._, A<string>._, A<object?>._))
            .Invokes(call =>
            {
                var rtId = call.GetArgument<OctoObjectId>(2);
                var attribute = call.GetArgument<string>(3)!;
                _rewrites.Add((rtId, attribute));
                var entity = _store.Values.SelectMany(l => l).Single(e => e.RtId == rtId);
                entity.SetAttributeRawValue(attribute, call.GetArgument<object?>(4));
            });
    }

    private static RtSecretValue ProtectWithK2(string plaintext)
    {
        return SecretTestModel.CreateProtector(activeKeyId: "k2").Protect(plaintext);
    }

    private SecretMaintenanceService CreateService(SecretAttributeProtector? protector = null)
    {
        protector ??= _protector;
        return new SecretMaintenanceService(_provider, _model.Cache, protector, new SecretWriteNormalizer(protector),
            NullLogger<SecretMaintenanceService>.Instance);
    }

    private static readonly string[] Plaintexts =
        ["plain-1", "api-1", "pw-3", "plain-4", "rec-plain", SecretTestModel.V1VectorPlaintext];

    private static void AssertResultCarriesNoValue(SecretSweepResult result)
    {
        var json = JsonSerializer.Serialize(result);
        foreach (var plaintext in Plaintexts)
        {
            Assert.DoesNotContain(plaintext, json);
        }

        Assert.DoesNotContain("enc:v", json);
    }

    [Fact]
    public async Task Verify_CountsEveryForm_AndWritesNothing()
    {
        var result = await CreateService().SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.Verify,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.CkTypesScanned); // Plain has no Secret slot
        Assert.Equal(5, result.EntitiesScanned);
        Assert.Equal(1, result.Totals.NotSet);
        Assert.Equal(2, result.Totals.Placeholder);
        Assert.Equal(3, result.Totals.Plaintext);
        Assert.Equal(1, result.Totals.EncV1);
        Assert.Equal(2, result.Totals.EncV2);
        Assert.Equal(1, result.Totals.EncV2ByKeyId["k1"]);
        Assert.Equal(1, result.Totals.EncV2ByKeyId["k2"]);
        Assert.Equal(2, result.Totals.UnknownKeyId);
        Assert.Equal(2, result.Totals.UnknownKeyIdByKeyId["k9"]);
        Assert.Equal(11, result.Totals.Total);
        Assert.Equal(0, result.ValuesRewritten);
        Assert.Empty(_rewrites);
        Assert.True(result.Success);

        var configType = _model.Config.CkTypeId.ToRtCkId().ToString();
        var credentialSlot = Assert.Single(result.Slots, s => s.CkTypeId == configType && s.AttributePath == "Credentials[].Value");
        Assert.Equal(1, credentialSlot.Counts.Plaintext);
        Assert.Equal(1, credentialSlot.Counts.UnknownKeyId);
        Assert.Contains(result.Slots, s => s.AttributePath == "Password" && s.CkTypeId == configType);
        AssertResultCarriesNoValue(result);
    }

    [Fact]
    public async Task Verify_WorksWithoutKeys()
    {
        var result = await CreateService(SecretTestModel.CreateProtector(configured: false))
            .SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.Verify, TestContext.Current.CancellationToken);

        Assert.Equal(11, result.Totals.Total);
        Assert.Equal(4, result.Totals.UnknownKeyId); // without a key ring every key id is unknown
    }

    [Theory]
    [InlineData(SecretSweepMode.Encrypt)]
    [InlineData(SecretSweepMode.Reprotect)]
    [InlineData(SecretSweepMode.ClearUnknownKid)]
    public async Task WritingModes_WithoutKeys_Throw(SecretSweepMode mode)
    {
        await Assert.ThrowsAsync<SecretEncryptionNotConfiguredException>(() =>
            CreateService(SecretTestModel.CreateProtector(configured: false))
                .SweepTenantAsync(SecretTestModel.TenantId, mode, TestContext.Current.CancellationToken));
        Assert.Empty(_rewrites);
    }

    [Fact]
    public async Task Encrypt_ProtectsLegacyValues_NormalisesPlaceholders_AndIsIdempotent()
    {
        var service = CreateService();

        var result = await service.SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.Encrypt,
            TestContext.Current.CancellationToken);

        Assert.Equal(6, result.ValuesRewritten);
        Assert.Equal(4, result.EntitiesRewritten);
        Assert.Equal("plain-1", _protector.Unprotect((RtSecretValue)_e1.Attributes["Password"]!));
        Assert.Null(_e2.Attributes["Password"]);
        Assert.Equal(SecretTestModel.V1VectorPlaintext, _protector.Unprotect((RtSecretValue)_e2.Attributes["ApiKey"]!));
        Assert.Equal("plain-4", _protector.Unprotect((RtSecretValue)_e4.Attributes["ApiKey"]!));
        var credentials = ((IEnumerable<RtRecord>)_e4.Attributes["Credentials"]!).ToList();
        Assert.Equal("rec-plain", _protector.Unprotect((RtSecretValue)credentials[1].Attributes["Value"]!));
        Assert.Equal(UnknownKidEnvelope, ((RtSecretValue)credentials[0].Attributes["Value"]!).Envelope);
        Assert.Equal("k2", ((RtSecretValue)_e3.Attributes["Password"]!).KeyId); // Encrypt leaves enc:v2 alone
        Assert.Null(_e5.Attributes["Password"]);
        Assert.Contains(result.Cleared, c => c.AttributePath == "Password" && c.PreviousForm == SecretValueForm.Placeholder);
        AssertResultCarriesNoValue(result);

        _rewrites.Clear();
        var second = await service.SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.Encrypt,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, second.ValuesRewritten);
        Assert.Empty(_rewrites);
        Assert.Equal(0, second.Totals.Plaintext + second.Totals.EncV1 + second.Totals.Placeholder);
    }

    [Fact]
    public async Task Reprotect_MovesOtherKeyIdsToTheActiveKey_AndCountsUnknownKeyIds()
    {
        var service = CreateService();
        await service.SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.Encrypt,
            TestContext.Current.CancellationToken);

        var result = await service.SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.Reprotect,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ValuesRewritten);
        var password = (RtSecretValue)_e3.Attributes["Password"]!;
        Assert.Equal("k1", password.KeyId);
        Assert.Equal("pw-3", _protector.Unprotect(password));
        Assert.Equal(2, result.Totals.UnknownKeyId);
        Assert.Equal(UnknownKidEnvelope, ((RtSecretValue)_e3.Attributes["ApiKey"]!).Envelope);
    }

    [Fact]
    public async Task ClearUnknownKid_SetsNull_AndReportsTheSlotsForReEntry()
    {
        var result = await CreateService().SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.ClearUnknownKid,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ValuesRewritten);
        Assert.Null(_e3.Attributes["ApiKey"]);
        var credentials = ((IEnumerable<RtRecord>)_e4.Attributes["Credentials"]!).ToList();
        Assert.Null(credentials[0].Attributes["Value"]);
        Assert.Equal("rec-plain", credentials[1].Attributes["Value"]); // untouched

        Assert.Equal(2, result.Cleared.Count);
        Assert.Contains(result.Cleared, c => c.RtId == _e3.RtId && c.AttributePath == "ApiKey" && c.KeyId == "k9" &&
                                             c.PreviousForm == SecretValueForm.UnknownKeyId);
        Assert.Contains(result.Cleared, c => c.RtId == _e4.RtId && c.AttributePath == "Credentials[Key=a].Value");
        AssertResultCarriesNoValue(result);
    }

    [Fact]
    public async Task Decrypt_RequiresConfirmation()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.Decrypt,
                TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.Decrypt, new SecretSweepOptions(),
                TestContext.Current.CancellationToken));
        Assert.Empty(_rewrites);
    }

    [Fact]
    public async Task Decrypt_Confirmed_WritesClearText_AndReportsUndecryptableValues()
    {
        var result = await CreateService().SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.Decrypt,
            new SecretSweepOptions { ConfirmDecrypt = true }, TestContext.Current.CancellationToken);

        Assert.Equal("api-1", _e1.Attributes["ApiKey"]);
        Assert.Equal(SecretTestModel.V1VectorPlaintext, _e2.Attributes["ApiKey"]);
        Assert.Equal("pw-3", _e3.Attributes["Password"]);
        Assert.Equal(3, result.ValuesRewritten);
        Assert.Equal(2, result.Totals.Failed); // the two unknown-kid values cannot be decrypted
        Assert.False(result.Success);
        Assert.All(result.Failures, f => Assert.Contains(nameof(UnknownSecretKeyIdException), f.Reason));
        AssertResultCarriesNoValue(result);
    }

    [Fact]
    public async Task TamperedValue_IsAFailure_WithoutValueInTheReason()
    {
        var tampered = ((RtSecretValue)_e3.Attributes["Password"]!).Envelope!;
        var flipped = tampered[..^2] + (tampered[^2] == 'A' ? "B" : "A") + tampered[^1];
        _e3.SetAttributeRawValue("Password", RtSecretValue.Protected(flipped));

        var result = await CreateService().SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.Reprotect,
            TestContext.Current.CancellationToken);

        var failure = Assert.Single(result.Failures, f => f.RtId == _e3.RtId);
        Assert.Equal("Password", failure.AttributePath);
        Assert.DoesNotContain("pw-3", failure.Reason);
        Assert.Equal(flipped, ((RtSecretValue)_e3.Attributes["Password"]!).Envelope); // untouched
        AssertResultCarriesNoValue(result);
    }

    [Fact]
    public async Task RewriteFailure_IsCountedAndNotReportedAsRewritten()
    {
        A.CallTo(() => _repository.RewriteAttributeValueForMigrationAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                _e1.RtId, "Password", A<object?>._))
            .Throws(new InvalidOperationException("storage down"));

        var result = await CreateService().SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.Encrypt,
            TestContext.Current.CancellationToken);

        Assert.Equal(5, result.ValuesRewritten);
        Assert.Equal(1, result.Totals.Failed);
        Assert.Contains(result.Failures, f => f.RtId == _e1.RtId && f.AttributePath == "Password" &&
                                              f.Reason.StartsWith(nameof(InvalidOperationException)));
    }

    [Fact]
    public async Task NormalizePlaceholders_OnlyTouchesPlaceholders_AndNeedsNoKey()
    {
        var service = CreateService(SecretTestModel.CreateProtector(configured: false));

        var result = await service.NormalizePlaceholdersAsync(SecretTestModel.TenantId, "Test",
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ValuesRewritten);
        Assert.Null(_e2.Attributes["Password"]);
        Assert.Null(_e5.Attributes["Password"]);
        Assert.Equal("plain-1", _e1.Attributes["Password"]);
        Assert.Equal(SecretTestModel.V1Vector, _e2.Attributes["ApiKey"]);
        Assert.Equal(2, result.Cleared.Count);
    }

    [Fact]
    public async Task ModelFilter_SkipsTypesOfOtherModels()
    {
        var result = await CreateService().SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.Verify,
            new SecretSweepOptions { CkModelName = "Other" }, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.CkTypesScanned);
        Assert.Equal(0, result.Totals.Total);
    }

    [Fact]
    public async Task SmallBatches_PageThroughAllEntities()
    {
        var result = await CreateService().SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.Verify,
            new SecretSweepOptions { BatchSize = 1 }, TestContext.Current.CancellationToken);

        Assert.Equal(5, result.EntitiesScanned);
        Assert.Equal(11, result.Totals.Total);
    }

    [Fact]
    public async Task UnknownTenant_Throws()
    {
        A.CallTo(() => _provider.GetRepositoryAsync("nope", A<CancellationToken>._))
            .Returns(Task.FromResult<IRuntimeRepository?>(null));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService().SweepTenantAsync("nope", SecretSweepMode.Verify, TestContext.Current.CancellationToken));
    }
}
