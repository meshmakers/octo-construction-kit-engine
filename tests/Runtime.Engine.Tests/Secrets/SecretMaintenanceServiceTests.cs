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
                var options = call.GetArgument<RtEntityQueryOptions>(2);
                var includeArchived = options?.GlobalFilter?.IncludeArchived ?? false;
                var all = (_store.TryGetValue(typeId.FullName, out var list) ? list : [])
                    .Where(e => includeArchived || e.RtState != RtState.Archived).ToList();
                // Like a real repository, a read hands out copies: the sweep must not see later writes
                // through the objects it read, and the store must not see the sweep's in-place changes.
                IResultSet<RtEntity> page = new ResultSet<RtEntity>(all.Skip(skip).Take(take).Select(Copy).ToList(),
                    all.Count, null, null);
                return Task.FromResult(page);
            });
        A.CallTo(() => _repository.RewriteAttributeValueIfUnchangedForMigrationAsync(A<IOctoSession>._,
                A<RtCkId<CkTypeId>>._, A<OctoObjectId>._, A<string>._, A<object?>._, A<object?>._))
            .ReturnsLazily(call =>
            {
                var rtId = call.GetArgument<OctoObjectId>(2);
                var attribute = call.GetArgument<string>(3)!;
                var entity = _store.Values.SelectMany(l => l).Single(e => e.RtId == rtId);
                _beforeConditionalRewrite?.Invoke(entity, attribute);
                entity.Attributes.TryGetValue(attribute, out var stored);
                if (!StoredAttributeValueComparer.AreEqual(stored, call.GetArgument<object?>(4)))
                {
                    return Task.FromResult(false);
                }

                _rewrites.Add((rtId, attribute));
                entity.SetAttributeRawValue(attribute, call.GetArgument<object?>(5));
                return Task.FromResult(true);
            });
    }

    /// <summary>
    ///     Runs inside the faked conditional rewrite before the compare - simulates a write that lands
    ///     between the sweep's read and its rewrite.
    /// </summary>
    private Action<RtEntity, string>? _beforeConditionalRewrite;

    private static RtEntity Copy(RtEntity entity)
    {
        var copy = new RtEntity(entity.CkTypeId!, entity.RtId) { RtState = entity.RtState };
        foreach (var (name, value) in entity.Attributes)
        {
            copy.SetAttributeRawValue(name, SecretMaintenanceService.CopyAttributeValue(value));
        }

        return copy;
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

        // No envelope (prefix with its separator); the key id "enc:v1" of SecretValueStates.LegacyV1KeyId is allowed.
        Assert.DoesNotContain("enc:v1:", json);
        Assert.DoesNotContain("enc:v2:", json);
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
        // Decisions 2026-10-06 item 2: unknown key ids are kept and listed as re-entry tasks.
        Assert.Equal(2, result.Unreadable.Count);
        Assert.Contains(result.Unreadable, u => u.RtId == _e3.RtId && u.AttributePath == "apiKey" && u.KeyId == "k9");
        Assert.Contains(result.Unreadable, u => u.RtId == _e4.RtId && u.AttributePath == "credentials[key=a].value" &&
                                                u.KeyId == "k9");
        Assert.Empty(result.Cleared);

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
        // AB#5532: key-free classification - without a key ring every key id is unknown and the enc:v1
        // string (no legacy key) is key-missing too; clear text stays clear text.
        Assert.Equal(5, result.Totals.UnknownKeyId);
        Assert.Equal(1, result.Totals.UnknownKeyIdByKeyId[SecretValueStates.LegacyV1KeyId]);
        Assert.Equal(0, result.Totals.EncV1);
        Assert.Equal(0, result.Totals.EncV2);
        Assert.Equal(3, result.Totals.Plaintext);
        Assert.Equal(0, result.Totals.Failed);
        Assert.True(result.Success);
        Assert.Equal(5, result.Unreadable.Count);
        Assert.Contains(result.Unreadable, u => u.RtId == _e1.RtId && u.AttributePath == "apiKey" && u.KeyId == "k1");
        Assert.Contains(result.Unreadable, u => u.RtId == _e3.RtId && u.AttributePath == "password" && u.KeyId == "k2");
        Assert.Contains(result.Unreadable,
            u => u.RtId == _e2.RtId && u.AttributePath == "apiKey" && u.KeyId == SecretValueStates.LegacyV1KeyId);
        Assert.Equal(0, result.ValuesRewritten);
        Assert.Empty(result.Cleared);
        Assert.Empty(_rewrites);
        AssertResultCarriesNoValue(result);
    }

    [Fact]
    public async Task Verify_WithoutLegacyKey_ListsEncV1AsUnreadable()
    {
        var result = await CreateService(SecretTestModel.CreateProtector(legacyV1Key: false))
            .SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.Verify, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Totals.EncV1);
        Assert.Equal(2, result.Totals.EncV2);
        Assert.Equal(3, result.Totals.UnknownKeyId);
        Assert.Equal(1, result.Totals.UnknownKeyIdByKeyId[SecretValueStates.LegacyV1KeyId]);
        Assert.Single(result.Unreadable, u => u.KeyId == SecretValueStates.LegacyV1KeyId);
        Assert.Empty(_rewrites);
    }

    [Fact]
    public async Task Encrypt_WithoutLegacyKey_KeepsEncV1_AndListsItForReEntry()
    {
        var result = await CreateService(SecretTestModel.CreateProtector(legacyV1Key: false))
            .SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.Encrypt, TestContext.Current.CancellationToken);

        // Not a failure: the enc:v1 value is kept (readable once the legacy key is configured).
        Assert.Equal(0, result.Totals.Failed);
        Assert.Equal(SecretTestModel.V1Vector, _e2.Attributes["ApiKey"]);
        Assert.DoesNotContain(_rewrites, r => r.RtId == _e2.RtId && r.Attribute == "ApiKey");
        Assert.Contains(result.Unreadable,
            u => u.RtId == _e2.RtId && u.AttributePath == "apiKey" && u.KeyId == SecretValueStates.LegacyV1KeyId);
        Assert.Empty(result.Cleared);
    }

    [Theory]
    [InlineData(SecretSweepMode.Encrypt)]
    [InlineData(SecretSweepMode.Reprotect)]
    [InlineData(SecretSweepMode.CleanupUnreadable)]
    public async Task WritingModes_WithoutKeys_Throw(SecretSweepMode mode)
    {
        await Assert.ThrowsAsync<SecretEncryptionNotConfiguredException>(() =>
            CreateService(SecretTestModel.CreateProtector(configured: false))
                .SweepTenantAsync(SecretTestModel.TenantId, mode, new SecretSweepOptions { ConfirmCleanupUnreadable = true },
                    TestContext.Current.CancellationToken));
        Assert.Empty(_rewrites);
    }

    [Theory]
    [InlineData(SecretSweepMode.Encrypt)]
    [InlineData(SecretSweepMode.Reprotect)]
    public async Task StrictMode_SweepStillConvertsLegacyPlaintext(SecretSweepMode mode)
    {
        var strict = SecretTestModel.CreateProtector(strictMode: true);
        Assert.Throws<LegacyPlaintextSecretRejectedException>(() =>
            strict.Unprotect((RtSecretValue)_e4.Attributes["ApiKey"]!));

        var result = await CreateService(strict).SweepTenantAsync(SecretTestModel.TenantId, mode,
            TestContext.Current.CancellationToken);

        Assert.Empty(result.Failures);
        Assert.Equal("plain-1", strict.Unprotect((RtSecretValue)_e1.Attributes["Password"]!));
        Assert.Equal("plain-4", strict.Unprotect((RtSecretValue)_e4.Attributes["ApiKey"]!));
        var credentials = ((IEnumerable<RtRecord>)_e4.Attributes["Credentials"]!).ToList();
        Assert.Equal("rec-plain", strict.Unprotect((RtSecretValue)credentials[1].Attributes["Value"]!));
        Assert.Equal(SecretTestModel.V1VectorPlaintext, strict.Unprotect((RtSecretValue)_e2.Attributes["ApiKey"]!));
    }

    [Fact]
    public async Task Encrypt_ProtectsLegacyValues_NormalisesPlaceholders_AndIsIdempotent()
    {
        var service = CreateService();

        var result = await service.SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.Encrypt,
            TestContext.Current.CancellationToken);

        Assert.Equal(6, result.ValuesRewritten);
        // AB#5532: 4 conversions to enc:v2 (3 plaintext, 1 enc:v1); the other 2 rewrites are placeholders.
        Assert.Equal(4, result.ValuesEncrypted);
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
        // Legacy placeholders were never set: they are counted, not listed for re-entry.
        Assert.Equal(2, result.PlaceholdersNormalized);
        Assert.Empty(result.Cleared);
        // Unknown key ids are kept (decisions 2026-10-06 item 2) and reported for re-entry.
        Assert.Equal(UnknownKidEnvelope, ((RtSecretValue)_e3.Attributes["ApiKey"]!).Envelope);
        Assert.Equal(2, result.Unreadable.Count);
        Assert.Equal(0, result.SkippedConcurrentlyModified);
        AssertResultCarriesNoValue(result);

        _rewrites.Clear();
        var second = await service.SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.Encrypt,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, second.ValuesRewritten);
        Assert.Equal(0, second.ValuesEncrypted);
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
        Assert.Equal(0, result.ValuesEncrypted); // a key rotation is no conversion of a legacy value
        var password = (RtSecretValue)_e3.Attributes["Password"]!;
        Assert.Equal("k1", password.KeyId);
        Assert.Equal("pw-3", _protector.Unprotect(password));
        Assert.Equal(2, result.Totals.UnknownKeyId);
        Assert.Equal(UnknownKidEnvelope, ((RtSecretValue)_e3.Attributes["ApiKey"]!).Envelope);
        Assert.Equal(2, result.Unreadable.Count);
        Assert.Empty(result.Cleared);
    }

    [Fact]
    public async Task CleanupUnreadable_RequiresConfirmation()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.CleanupUnreadable,
                TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.CleanupUnreadable,
                new SecretSweepOptions { ConfirmDecrypt = true }, TestContext.Current.CancellationToken));
        Assert.Empty(_rewrites);
        Assert.Equal(UnknownKidEnvelope, ((RtSecretValue)_e3.Attributes["ApiKey"]!).Envelope);
    }

    [Fact]
    public void CleanupUnreadable_KeepsTheNumericValueOfTheFormerClearUnknownKid()
    {
        Assert.Equal(3, (int)SecretSweepMode.CleanupUnreadable);
    }

    private RtEntity AddArchivedEntity()
    {
        var deleted = _model.NewConfig();
        deleted.RtState = RtState.Archived;
        deleted.SetAttributeRawValue("Password", "plain-1");
        deleted.SetAttributeRawValue("ApiKey", RtSecretValue.Protected(UnknownKidEnvelope));
        _store[_model.Config.CkTypeId.ToRtCkId().FullName].Add(deleted);
        return deleted;
    }

    /// <summary>
    ///     AB#5532/AB#5544 (PO decision): archived (deleted) entities are swept - no clear text stays at rest -
    ///     but their unreadable values are no re-entry tasks; they are counted separately.
    /// </summary>
    [Theory]
    [InlineData(SecretSweepMode.Verify)]
    [InlineData(SecretSweepMode.Encrypt)]
    [InlineData(SecretSweepMode.Reprotect)]
    public async Task ArchivedEntities_AreProcessed_ButNotListedForReEntry(SecretSweepMode mode)
    {
        var deleted = AddArchivedEntity();

        var result = await CreateService().SweepTenantAsync(SecretTestModel.TenantId, mode,
            TestContext.Current.CancellationToken);

        Assert.Equal(6, result.EntitiesScanned);
        Assert.Equal(1, result.ArchivedEntitiesScanned);
        Assert.Equal(1, result.ArchivedUnreadableValues);
        Assert.DoesNotContain(result.Unreadable, u => u.RtId == deleted.RtId);
        Assert.Equal(3, result.Totals.UnknownKeyId); // e3, e4 record and the archived entity
        Assert.Equal(2, result.Unreadable.Count);
        Assert.Equal(UnknownKidEnvelope, ((RtSecretValue)deleted.Attributes["ApiKey"]!).Envelope); // kept

        if (mode == SecretSweepMode.Verify)
        {
            Assert.Equal("plain-1", deleted.Attributes["Password"]);
        }
        else
        {
            // The clear text of the deleted entity is encrypted at rest as well.
            var password = Assert.IsType<RtSecretValue>(deleted.Attributes["Password"]);
            Assert.True(_protector.IsProtectedEnvelope(password.Envelope!));
            Assert.Contains(_rewrites, r => r.RtId == deleted.RtId && r.Attribute == "Password");
        }

        AssertResultCarriesNoValue(result);
    }

    [Fact]
    public async Task ArchivedEntities_CleanupUnreadable_Deletes_ButDoesNotListAsCleared()
    {
        var deleted = AddArchivedEntity();

        var result = await CreateService().SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.CleanupUnreadable,
            new SecretSweepOptions { ConfirmCleanupUnreadable = true }, TestContext.Current.CancellationToken);

        Assert.Null(deleted.Attributes["ApiKey"]);
        Assert.Equal(1, result.ArchivedUnreadableValues);
        Assert.Equal(2, result.Cleared.Count);
        Assert.DoesNotContain(result.Cleared, c => c.RtId == deleted.RtId);
        Assert.Empty(result.Unreadable);
        AssertResultCarriesNoValue(result);
    }

    [Fact]
    public async Task CleanupUnreadable_Confirmed_SetsNull_AndReportsTheSlotsForReEntry()
    {
        var result = await CreateService().SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.CleanupUnreadable,
            new SecretSweepOptions { ConfirmCleanupUnreadable = true }, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ValuesRewritten);
        Assert.Null(_e3.Attributes["ApiKey"]);
        var credentials = ((IEnumerable<RtRecord>)_e4.Attributes["Credentials"]!).ToList();
        Assert.Null(credentials[0].Attributes["Value"]);
        Assert.Equal("rec-plain", credentials[1].Attributes["Value"]); // untouched

        Assert.Equal(2, result.Cleared.Count);
        Assert.Contains(result.Cleared, c => c.RtId == _e3.RtId && c.AttributePath == "apiKey" && c.KeyId == "k9" &&
                                             c.PreviousForm == SecretValueForm.UnknownKeyId);
        Assert.Contains(result.Cleared, c => c.RtId == _e4.RtId && c.AttributePath == "credentials[key=a].value");
        Assert.Empty(result.Unreadable);
        AssertResultCarriesNoValue(result);
    }

    /// <summary>
    ///     PO decision (AB#5532): an enc:v1 value that is unreadable only because the legacy key is not configured
    ///     is a configuration gap, not key loss - CleanupUnreadable keeps it, lists it as unreadable and counts it
    ///     separately; enc:v2 values of unknown key ids are still deleted.
    /// </summary>
    [Fact]
    public async Task CleanupUnreadable_WithoutLegacyKey_KeepsEncV1_AndCountsItSeparately()
    {
        var result = await CreateService(SecretTestModel.CreateProtector(legacyV1Key: false)).SweepTenantAsync(
            SecretTestModel.TenantId, SecretSweepMode.CleanupUnreadable,
            new SecretSweepOptions { ConfirmCleanupUnreadable = true }, TestContext.Current.CancellationToken);

        Assert.Equal(SecretTestModel.V1Vector, _e2.Attributes["ApiKey"]);
        Assert.DoesNotContain(_rewrites, r => r.RtId == _e2.RtId && r.Attribute == "ApiKey");
        Assert.Equal(1, result.SkippedLegacyV1KeyMissing);
        var kept = Assert.Single(result.Unreadable);
        Assert.Equal(_e2.RtId, kept.RtId);
        Assert.Equal("apiKey", kept.AttributePath);
        Assert.Equal(SecretValueStates.LegacyV1KeyId, kept.KeyId);
        Assert.DoesNotContain(result.Cleared, c => c.KeyId == SecretValueStates.LegacyV1KeyId);

        // The enc:v2 values of an unknown key id are deleted as before.
        Assert.Equal(2, result.ValuesRewritten);
        Assert.Equal(2, result.Cleared.Count);
        Assert.Null(_e3.Attributes["ApiKey"]);
        Assert.Equal(0, result.Totals.Failed);
        AssertResultCarriesNoValue(result);
    }

    [Fact]
    public async Task CleanupUnreadable_WithLegacyKey_DoesNotCountSkippedLegacyValues()
    {
        var result = await CreateService().SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.CleanupUnreadable,
            new SecretSweepOptions { ConfirmCleanupUnreadable = true }, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.SkippedLegacyV1KeyMissing);
        Assert.Equal(SecretTestModel.V1Vector, _e2.Attributes["ApiKey"]); // readable enc:v1 is not unreadable
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
        Assert.Equal("password", failure.AttributePath);
        Assert.DoesNotContain("pw-3", failure.Reason);
        Assert.Equal(flipped, ((RtSecretValue)_e3.Attributes["Password"]!).Envelope); // untouched
        AssertResultCarriesNoValue(result);
    }

    [Fact]
    public async Task RewriteFailure_IsCountedAndNotReportedAsRewritten()
    {
        A.CallTo(() => _repository.RewriteAttributeValueIfUnchangedForMigrationAsync(A<IOctoSession>._,
                A<RtCkId<CkTypeId>>._, _e1.RtId, "Password", A<object?>._, A<object?>._))
            .Throws(new InvalidOperationException("storage down"));

        var result = await CreateService().SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.Encrypt,
            TestContext.Current.CancellationToken);

        Assert.Equal(5, result.ValuesRewritten);
        Assert.Equal(1, result.Totals.Failed);
        Assert.Contains(result.Failures, f => f.RtId == _e1.RtId && f.AttributePath == "password" &&
                                              f.Reason.StartsWith(nameof(InvalidOperationException)));
    }

    [Fact]
    public async Task CleanupUnreadable_RewriteFailure_KeepsTheValueInTheUnreadableList()
    {
        A.CallTo(() => _repository.RewriteAttributeValueIfUnchangedForMigrationAsync(A<IOctoSession>._,
                A<RtCkId<CkTypeId>>._, _e3.RtId, "ApiKey", A<object?>._, A<object?>._))
            .Throws(new InvalidOperationException("storage down"));

        var result = await CreateService().SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.CleanupUnreadable,
            new SecretSweepOptions { ConfirmCleanupUnreadable = true }, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ValuesRewritten);
        var cleared = Assert.Single(result.Cleared);
        Assert.Equal(_e4.RtId, cleared.RtId);
        var unreadable = Assert.Single(result.Unreadable);
        Assert.Equal(_e3.RtId, unreadable.RtId);
        Assert.Equal("apiKey", unreadable.AttributePath);
        Assert.Equal("k9", unreadable.KeyId);
    }

    /// <summary>
    ///     Decisions 2026-10-06 item 1: only a LEGACY string is a placeholder; a protected value is never
    ///     normalised, whatever it decrypts to.
    /// </summary>
    [Fact]
    public async Task Encrypt_DoesNotNormaliseAProtectedPlaceholderLookingValue()
    {
        var protectedPlaceholder = _protector.Protect("TODO_SET_PASSWORD");
        _e5.SetAttributeRawValue("Password", protectedPlaceholder);

        var result = await CreateService().SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.Encrypt,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.PlaceholdersNormalized); // only _e2's legacy TODO_SET_PASSWORD
        Assert.Equal(protectedPlaceholder, _e5.Attributes["Password"]);
        Assert.Null(_e2.Attributes["Password"]);
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
        Assert.Equal(2, result.PlaceholdersNormalized);
        Assert.Empty(result.Cleared);
    }

    [Fact]
    public async Task ConcurrentlyModifiedSecret_IsSkipped_AndTheNewerValueSurvives()
    {
        var newer = _protector.Protect("set-in-between");
        _beforeConditionalRewrite = (entity, attribute) =>
        {
            if (entity.RtId == _e1.RtId && attribute == "Password")
            {
                entity.SetAttributeRawValue("Password", newer);
            }
        };

        var result = await CreateService().SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.Encrypt,
            TestContext.Current.CancellationToken);

        Assert.Same(newer, _e1.Attributes["Password"]);
        Assert.Equal(1, result.SkippedConcurrentlyModified);
        Assert.Equal(5, result.ValuesRewritten); // the skipped value is not counted as rewritten
        Assert.Equal(3, result.ValuesEncrypted); // nor as encrypted
        Assert.True(result.Success);             // and it is not a failure
        Assert.DoesNotContain(_rewrites, r => r.RtId == _e1.RtId && r.Attribute == "Password");
        AssertResultCarriesNoValue(result);
    }

    [Fact]
    public async Task ConcurrentlyModifiedPlaceholder_IsSkipped_AndNotCountedAsNormalised()
    {
        _beforeConditionalRewrite = (entity, attribute) =>
        {
            if (entity.RtId == _e2.RtId && attribute == "Password")
            {
                entity.SetAttributeRawValue("Password", _protector.Protect("entered-meanwhile"));
            }
        };

        var result = await CreateService().SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.Encrypt,
            TestContext.Current.CancellationToken);

        Assert.Equal("entered-meanwhile", _protector.Unprotect((RtSecretValue)_e2.Attributes["Password"]!));
        Assert.Equal(1, result.PlaceholdersNormalized); // only _e5's placeholder
        Assert.Equal(1, result.SkippedConcurrentlyModified);
        Assert.Equal(4, result.ValuesEncrypted);
    }

    [Fact]
    public async Task ConcurrentlyModifiedRecordArray_IsSkipped_AsAWhole()
    {
        // Another writer replaces a non-secret member of the record array after the sweep read it.
        _beforeConditionalRewrite = (entity, attribute) =>
        {
            if (entity.RtId == _e4.RtId && attribute == "Credentials")
            {
                entity.SetAttributeRawValue("Credentials", new List<RtRecord>
                {
                    _model.CredentialRecord("a", RtSecretValue.Protected(UnknownKidEnvelope)),
                    _model.CredentialRecord("b", "rec-plain"),
                    _model.CredentialRecord("c", "added-meanwhile")
                });
            }
        };

        var result = await CreateService().SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.Encrypt,
            TestContext.Current.CancellationToken);

        var credentials = ((IEnumerable<RtRecord>)_e4.Attributes["Credentials"]!).ToList();
        Assert.Equal(3, credentials.Count);
        Assert.Equal("rec-plain", credentials[1].Attributes["Value"]); // the old array was not written back
        Assert.Equal(1, result.SkippedConcurrentlyModified);
        Assert.Equal(5, result.ValuesRewritten);
        Assert.Equal(3, result.ValuesEncrypted);
    }

    [Fact]
    public async Task UnchangedRecordArray_IsRewritten_WithTheValueReadAsExpectedValue()
    {
        var calls = new List<object?>();
        A.CallTo(() => _repository.RewriteAttributeValueIfUnchangedForMigrationAsync(A<IOctoSession>._,
                A<RtCkId<CkTypeId>>._, _e4.RtId, "Credentials", A<object?>._, A<object?>._))
            .Invokes(call => calls.Add(call.GetArgument<object?>(4)))
            .Returns(Task.FromResult(true));

        await CreateService().SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.Encrypt,
            TestContext.Current.CancellationToken);

        // The expected value is the record array as read - with the plaintext member, not the encrypted one.
        var expected = Assert.Single(calls);
        var records = ((IEnumerable<object?>)expected!).Cast<RtRecord>().ToList();
        Assert.Equal("rec-plain", records[1].Attributes["Value"]);
        Assert.True(StoredAttributeValueComparer.AreEqual(_e4.Attributes["Credentials"], expected));
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
    public async Task Paging_UsesADeterministicRtIdOrder_AndIncludesArchivedEntities()
    {
        var seen = new List<RtEntityQueryOptions>();
        A.CallTo(() => _repository.GetRtEntitiesByTypeAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                A<RtEntityQueryOptions>._, A<int?>._, A<int?>._))
            .Invokes(call => seen.Add(call.GetArgument<RtEntityQueryOptions>(2)!))
            .Returns(Task.FromResult<IResultSet<RtEntity>>(new ResultSet<RtEntity>([], 0, null, null)));

        await CreateService().SweepTenantAsync(SecretTestModel.TenantId, SecretSweepMode.Verify,
            TestContext.Current.CancellationToken);

        Assert.NotEmpty(seen);
        Assert.All(seen, options =>
        {
            var sort = Assert.Single(options.SortOrders!);
            Assert.Equal(SecretMaintenanceService.RtIdSortPath, sort.AttributePath);
            Assert.Equal(SortOrders.Ascending, sort.SortOrder);
            Assert.True(options.GlobalFilter?.IncludeArchived);
        });
    }

    [Fact]
    public async Task UnknownTenant_Throws()
    {
        A.CallTo(() => _provider.GetRepositoryAsync("nope", A<CancellationToken>._))
            .Returns(Task.FromResult<IRuntimeRepository?>(null));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService().SweepTenantAsync("nope", SecretSweepMode.Verify, TestContext.Current.CancellationToken));
    }

    #region AB#5532 enc:v2 as legacy string (decryption oracle)

    [Theory]
    [InlineData(SecretSweepMode.Verify)]
    [InlineData(SecretSweepMode.Encrypt)]
    [InlineData(SecretSweepMode.Reprotect)]
    [InlineData(SecretSweepMode.CleanupUnreadable)]
    [InlineData(SecretSweepMode.Decrypt)]
    public async Task LegacyStringWithV2Envelope_IsReportedAsFailed_AndLeftAsStored(SecretSweepMode mode)
    {
        var foreign = _protector.Protect("victim-secret").Envelope!;
        var foreignUnknownKid = UnknownKidEnvelope;
        var copy = _model.NewConfig();
        copy.SetAttributeRawValue("Password", foreign);
        copy.SetAttributeRawValue("ApiKey", RtSecretValue.LegacyPlaintext(foreignUnknownKid));
        copy.SetAttributeRawValue("Credentials", new List<RtRecord> { _model.CredentialRecord("x", foreign) });
        _store[_model.Config.CkTypeId.ToRtCkId().FullName] = [copy];

        var result = await CreateService().SweepTenantAsync(SecretTestModel.TenantId, mode,
            new SecretSweepOptions { ConfirmDecrypt = true, ConfirmCleanupUnreadable = true },
            TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Totals.Failed);
        Assert.Equal(0, result.Totals.EncV2);
        Assert.Equal(0, result.Totals.UnknownKeyId);
        Assert.Equal(3, result.Failures.Count(f => f.RtId == copy.RtId && f.Reason.Contains("enc:v2")));
        Assert.Empty(result.Cleared);
        Assert.DoesNotContain(_rewrites, r => r.RtId == copy.RtId);
        Assert.Equal(foreign, copy.Attributes["Password"]);
        Assert.Equal(foreignUnknownKid, ((RtSecretValue)copy.Attributes["ApiKey"]!).RawValue);
        var element = ((IEnumerable<RtRecord>)copy.Attributes["Credentials"]!).Single();
        Assert.Equal(foreign, element.Attributes["Value"]);
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("victim-secret", json);
        Assert.DoesNotContain(foreign, json);
    }

    #endregion
}
