using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Secrets;
using Meshmakers.Octo.Runtime.Engine.Secrets;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Secrets;

/// <summary>
///     AB#5532: the Secret write rules (concept §3.6) and the record carry-over (§4.6).
/// </summary>
public class SecretWriteNormalizerTests
{
    private const string Plain = "s3cr3t-Plain!";
    private readonly SecretTestModel _model = new();
    private readonly SecretAttributeProtector _protector = SecretTestModel.CreateProtector();
    private readonly SecretWriteNormalizer _normalizer;

    public SecretWriteNormalizerTests()
    {
        _normalizer = new SecretWriteNormalizer(_protector);
    }

    private SecretWriteResult Normalize(RtEntity entity, SecretWriteOperation operation, RtEntity? stored = null,
        SecretValueOrigin origin = SecretValueOrigin.Input)
    {
        return _normalizer.Normalize(_model.Cache, SecretTestModel.TenantId, _model.Config, entity, operation, stored,
            origin);
    }

    private string Decrypt(object? value)
    {
        var secret = Assert.IsType<RtSecretValue>(value);
        Assert.True(secret.IsProtected);
        return _protector.Unprotect(secret);
    }

    private void AssertNoPlaintext(RtEntity entity, params string[] plaintexts)
    {
        foreach (var slot in _model.SecretSlotValues(entity))
        {
            Assert.True(slot == null || slot is RtSecretValue { IsProtected: true },
                $"Secret slot holds {slot?.GetType().Name} instead of null/Protected");
        }

        var strings = SecretTestModel.AllStrings(entity).ToList();
        foreach (var plaintext in plaintexts)
        {
            Assert.DoesNotContain(plaintext, strings);
        }
    }

    #region Top-level rules

    [Fact]
    public void Insert_NonEmptyString_IsEncryptedWithActiveKey()
    {
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("ApiKey", Plain);

        var result = Normalize(entity, SecretWriteOperation.Insert);

        var secret = Assert.IsType<RtSecretValue>(entity.Attributes["ApiKey"]);
        Assert.Equal("k1", secret.KeyId);
        Assert.Equal(Plain, Decrypt(secret));
        Assert.Equal(1, result.ProtectedCount);
        Assert.Empty(result.MissingRequiredAttributes);
        AssertNoPlaintext(entity, Plain);
    }

    [Fact]
    public void Insert_PendingValue_IsEncrypted()
    {
        var entity = _model.NewConfig();
        entity.SetAttributeValue("ApiKey", ConstructionKit.Contracts.DataTransferObjects.AttributeValueTypesDto.Secret,
            Plain);
        Assert.True(((RtSecretValue)entity.Attributes["ApiKey"]!).IsPending);

        Normalize(entity, SecretWriteOperation.Insert);

        Assert.Equal(Plain, Decrypt(entity.Attributes["ApiKey"]));
    }

    [Fact]
    public void Insert_ValueThatLooksLikeAnEnvelope_IsEncryptedAsIs()
    {
        // Public input is always encrypted - ciphertext cannot be copied onto another entity.
        var other = _protector.Protect("other").Envelope!;
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("ApiKey", other);

        Normalize(entity, SecretWriteOperation.Insert);

        Assert.Equal(other, Decrypt(entity.Attributes["ApiKey"]));
    }

    [Theory]
    [InlineData(SecretWriteOperation.Insert)]
    [InlineData(SecretWriteOperation.Update)]
    public void EmptyString_IsRemovedFromTheWrite(SecretWriteOperation operation)
    {
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("Password", "");

        Normalize(entity, operation);

        Assert.False(entity.Attributes.ContainsKey("Password"));
    }

    [Theory]
    [InlineData("<SET_AFTER_INSTALL>")]
    [InlineData("TODO_SET_PASSWORD")]
    [InlineData(" <x> ")]
    public void Placeholder_IsStoredAsNull(string placeholder)
    {
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("Password", placeholder);

        Normalize(entity, SecretWriteOperation.Update);

        Assert.True(entity.Attributes.ContainsKey("Password"));
        Assert.Null(entity.Attributes["Password"]);
    }

    [Fact]
    public void Protected_IsPassedThrough()
    {
        var protectedValue = _protector.Protect(Plain);
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("ApiKey", protectedValue);

        var result = Normalize(entity, SecretWriteOperation.Insert);

        Assert.Same(protectedValue, entity.Attributes["ApiKey"]);
        Assert.Equal(0, result.ProtectedCount);
    }

    [Fact]
    public void ProtectedWithUnknownKeyId_IsPassedThrough()
    {
        // Trusted callers (restore) may write values of another environment; the sweep handles them.
        var foreign = RtSecretValue.Protected("enc:v2:k9:AAECAwQFBgcICQoLce30XInwk1La5ADQECWjFW2r_6nUXsA");
        var entity = _model.NewOptionalOnly();
        entity.SetAttributeRawValue("Password", foreign);

        _normalizer.Normalize(_model.Cache, SecretTestModel.TenantId, _model.OptionalOnly, entity,
            SecretWriteOperation.Insert);

        Assert.Same(foreign, entity.Attributes["Password"]);
    }

    [Fact]
    public void Null_Clears()
    {
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("Password", null);

        Normalize(entity, SecretWriteOperation.Update);

        Assert.True(entity.Attributes.ContainsKey("Password"));
        Assert.Null(entity.Attributes["Password"]);
    }

    [Fact]
    public void LegacyPlaintext_IsEncrypted()
    {
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("ApiKey", RtSecretValue.LegacyPlaintext(Plain));

        Normalize(entity, SecretWriteOperation.Insert);

        Assert.Equal(Plain, Decrypt(entity.Attributes["ApiKey"]));
    }

    [Fact]
    public void LegacyEncV1_IsDecryptedAndReEncrypted_NotDoubleWrapped()
    {
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("ApiKey", RtSecretValue.LegacyPlaintext(SecretTestModel.V1Vector));

        Normalize(entity, SecretWriteOperation.Insert);

        Assert.Equal(SecretTestModel.V1VectorPlaintext, Decrypt(entity.Attributes["ApiKey"]));
    }

    [Fact]
    public void StorageOrigin_StringIsLegacy_EncV1IsNotDoubleWrapped()
    {
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("ApiKey", SecretTestModel.V1Vector);

        Normalize(entity, SecretWriteOperation.Insert, origin: SecretValueOrigin.Storage);

        Assert.Equal(SecretTestModel.V1VectorPlaintext, Decrypt(entity.Attributes["ApiKey"]));
    }

    [Fact]
    public void LegacyPlaceholder_IsNotSet()
    {
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("Password", RtSecretValue.LegacyPlaintext("TODO_SET_PASSWORD"));

        Normalize(entity, SecretWriteOperation.Update);

        Assert.Null(entity.Attributes["Password"]);
    }

    #endregion

    #region Replace carry-over

    [Fact]
    public void Replace_EmptyString_CarriesOverStoredValue()
    {
        var storedValue = _protector.Protect(Plain);
        var stored = _model.NewConfig();
        stored.SetAttributeRawValue("ApiKey", storedValue);
        var incoming = _model.NewConfig(stored.RtId);
        incoming.SetAttributeRawValue("ApiKey", "");

        var result = Normalize(incoming, SecretWriteOperation.Replace, stored);

        Assert.Equal(storedValue.Envelope, ((RtSecretValue)incoming.Attributes["ApiKey"]!).Envelope);
        Assert.Equal(1, result.CarriedOverCount);
        Assert.Empty(result.MissingRequiredAttributes);
    }

    [Fact]
    public void Replace_Omitted_CarriesOverStoredValue()
    {
        var storedValue = _protector.Protect(Plain);
        var stored = _model.NewConfig();
        stored.SetAttributeRawValue("ApiKey", storedValue);
        stored.SetAttributeRawValue("Password", _protector.Protect("pw"));
        var incoming = _model.NewConfig(stored.RtId);

        Normalize(incoming, SecretWriteOperation.Replace, stored);

        Assert.Equal(storedValue.Envelope, ((RtSecretValue)incoming.Attributes["ApiKey"]!).Envelope);
        Assert.Equal("pw", Decrypt(incoming.Attributes["Password"]));
    }

    [Fact]
    public void Replace_Null_Clears()
    {
        var stored = _model.NewConfig();
        stored.SetAttributeRawValue("ApiKey", _protector.Protect(Plain));
        stored.SetAttributeRawValue("Password", _protector.Protect("pw"));
        var incoming = _model.NewConfig(stored.RtId);
        incoming.SetAttributeRawValue("ApiKey", "new");
        incoming.SetAttributeRawValue("Password", null);

        Normalize(incoming, SecretWriteOperation.Replace, stored);

        Assert.Null(incoming.Attributes["Password"]);
        Assert.Equal("new", Decrypt(incoming.Attributes["ApiKey"]));
    }

    [Fact]
    public void Replace_StoredLegacyString_IsCarriedOverAsLegacy_NotAsInput()
    {
        var stored = _model.NewConfig();
        stored.SetAttributeRawValue("ApiKey", SecretTestModel.V1Vector); // legacy string slot
        var incoming = _model.NewConfig(stored.RtId);

        Normalize(incoming, SecretWriteOperation.Replace, stored);

        Assert.Equal(SecretTestModel.V1VectorPlaintext, Decrypt(incoming.Attributes["ApiKey"]));
    }

    [Fact]
    public void Replace_RequiredSecretWithoutValueAndNothingStored_IsReportedMissing()
    {
        var incoming = _model.NewConfig();
        incoming.SetAttributeRawValue("ApiKey", "");

        var result = Normalize(incoming, SecretWriteOperation.Replace, null);

        Assert.Equal(["ApiKey"], result.MissingRequiredAttributes);
    }

    [Fact]
    public void Update_RequiredSecretSetToNull_IsReportedMissing()
    {
        var incoming = _model.NewConfig();
        incoming.SetAttributeRawValue("ApiKey", null);

        var result = Normalize(incoming, SecretWriteOperation.Update);

        Assert.Equal(["ApiKey"], result.MissingRequiredAttributes);
    }

    #endregion

    #region Records

    [Fact]
    public void RecordArray_NullOrEmptySubValue_IsCarriedOverByRecordKey()
    {
        var protectedA = _protector.Protect("secret-a");
        var protectedB = _protector.Protect("secret-b");
        var stored = _model.NewConfig();
        stored.SetAttributeRawValue("ApiKey", _protector.Protect(Plain));
        stored.SetAttributeRawValue("Credentials", new List<RtRecord>
        {
            _model.CredentialRecord("a", protectedA),
            _model.CredentialRecord("b", protectedB)
        });

        // Reordered, one null, one "", one omitted-key-new element, one new value.
        var incoming = _model.NewConfig(stored.RtId);
        incoming.SetAttributeRawValue("Credentials", new List<RtRecord>
        {
            _model.CredentialRecord("b", ""),
            _model.CredentialRecord("a", null),
            _model.CredentialRecord("c", "secret-c"),
            _model.CredentialRecord("d", null, includeValue: false)
        });

        Normalize(incoming, SecretWriteOperation.Update, stored);

        var elements = ((IEnumerable<RtRecord>)incoming.Attributes["Credentials"]!).ToList();
        Assert.Equal(protectedB.Envelope, ((RtSecretValue)elements[0].Attributes["Value"]!).Envelope);
        Assert.Equal(protectedA.Envelope, ((RtSecretValue)elements[1].Attributes["Value"]!).Envelope);
        Assert.Equal("secret-c", Decrypt(elements[2].Attributes["Value"]));
        Assert.False(elements[3].Attributes.ContainsKey("Value"));
        AssertNoPlaintext(incoming, "secret-c");
    }

    [Fact]
    public void RecordArray_IntegerKeysOfDifferentClrTypes_Match()
    {
        Assert.True(SecretWriteNormalizer.RecordKeyEquals(5, 5L));
        Assert.True(SecretWriteNormalizer.RecordKeyEquals("x", "x"));
        Assert.False(SecretWriteNormalizer.RecordKeyEquals("x", "X"));
    }

    [Fact]
    public void RecordArray_Placeholder_ClearsTheStoredSubValue()
    {
        var stored = _model.NewConfig();
        stored.SetAttributeRawValue("Credentials", new List<RtRecord> { _model.CredentialRecord("a", _protector.Protect("x")) });
        var incoming = _model.NewConfig(stored.RtId);
        incoming.SetAttributeRawValue("Credentials", new List<RtRecord> { _model.CredentialRecord("a", "<CLEAR>") });

        Normalize(incoming, SecretWriteOperation.Replace, stored);

        var element = ((IEnumerable<RtRecord>)incoming.Attributes["Credentials"]!).Single();
        Assert.Null(element.Attributes["Value"]);
    }

    [Fact]
    public void SingleRecord_IsCarriedOverByPosition()
    {
        var storedValue = _protector.Protect("primary");
        var stored = _model.NewConfig();
        stored.SetAttributeRawValue("Primary", _model.CredentialRecord("old-key", storedValue));
        var incoming = _model.NewConfig(stored.RtId);
        incoming.SetAttributeRawValue("Primary", _model.CredentialRecord("new-key", null));

        Normalize(incoming, SecretWriteOperation.Update, stored);

        var record = (RtRecord)incoming.Attributes["Primary"]!;
        Assert.Equal(storedValue.Envelope, ((RtSecretValue)record.Attributes["Value"]!).Envelope);
    }

    [Fact]
    public void NestedRecords_AreCarriedOverByKeyAndPosition()
    {
        var innerValue = _protector.Protect("inner");
        var itemValue = _protector.Protect("item");
        var stored = _model.NewConfig();
        stored.SetAttributeRawValue("Wrappers", new List<RtRecord>
        {
            _model.WrapperRecord("w1", _model.CredentialRecord("i", innerValue), _model.CredentialRecord("k", itemValue))
        });
        var incoming = _model.NewConfig(stored.RtId);
        incoming.SetAttributeRawValue("Wrappers", new List<RtRecord>
        {
            _model.WrapperRecord("w1", _model.CredentialRecord("i", ""),
                _model.CredentialRecord("k", null), _model.CredentialRecord("new", "fresh"))
        });

        Normalize(incoming, SecretWriteOperation.Replace, stored);

        var wrapper = ((IEnumerable<RtRecord>)incoming.Attributes["Wrappers"]!).Single();
        var inner = (RtRecord)wrapper.Attributes["Inner"]!;
        var items = ((IEnumerable<RtRecord>)wrapper.Attributes["Items"]!).ToList();
        Assert.Equal(innerValue.Envelope, ((RtSecretValue)inner.Attributes["Value"]!).Envelope);
        Assert.Equal(itemValue.Envelope, ((RtSecretValue)items[0].Attributes["Value"]!).Envelope);
        Assert.Equal("fresh", Decrypt(items[1].Attributes["Value"]));
        AssertNoPlaintext(incoming, "fresh");
    }

    [Fact]
    public void RequiredRecordSecret_WithoutValueAndNoCounterpart_IsReportedWithKeyPath()
    {
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("ApiKey", Plain);
        var record = new RtRecord { CkRecordId = _model.RequiredCredential.CkRecordId.ToRtCkId() };
        record.SetAttributeRawValue("Key", "smtp");
        record.SetAttributeRawValue("Secret", "");
        entity.SetAttributeRawValue("RequiredCredentials", new List<RtRecord> { record });

        var result = Normalize(entity, SecretWriteOperation.Insert);

        Assert.Equal(["RequiredCredentials[Key=smtp].Secret"], result.MissingRequiredAttributes);
    }

    #endregion

    #region Keys and helpers

    [Fact]
    public void NoKeyConfigured_NonEmptySecret_Throws_WithoutEchoingTheValue()
    {
        var normalizer = new SecretWriteNormalizer(SecretTestModel.CreateProtector(configured: false));
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("ApiKey", Plain);

        var ex = Assert.Throws<SecretEncryptionNotConfiguredException>(() =>
            normalizer.Normalize(_model.Cache, SecretTestModel.TenantId, _model.Config, entity,
                SecretWriteOperation.Insert));

        Assert.DoesNotContain(Plain, ex.Message);
        Assert.DoesNotContain(Plain, ex.ToString());
    }

    [Fact]
    public void NoKeyConfigured_EmptyNullPlaceholderAndLegacy_StillWork()
    {
        var normalizer = new SecretWriteNormalizer(SecretTestModel.CreateProtector(configured: false));
        var legacy = RtSecretValue.LegacyPlaintext("stored-before");
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("Password", "<SET>");
        entity.SetAttributeRawValue("ApiKey", legacy);
        var other = _model.NewOptionalOnly();
        other.SetAttributeRawValue("Password", "");
        var third = _model.NewOptionalOnly();
        third.SetAttributeRawValue("Password", null);

        normalizer.Normalize(_model.Cache, SecretTestModel.TenantId, _model.Config, entity, SecretWriteOperation.Update);
        normalizer.Normalize(_model.Cache, SecretTestModel.TenantId, _model.OptionalOnly, other, SecretWriteOperation.Update);
        normalizer.Normalize(_model.Cache, SecretTestModel.TenantId, _model.OptionalOnly, third, SecretWriteOperation.Update);

        Assert.Null(entity.Attributes["Password"]);
        Assert.Same(legacy, entity.Attributes["ApiKey"]); // already stored that way - kept until the sweep
        Assert.False(other.Attributes.ContainsKey("Password"));
        Assert.Null(third.Attributes["Password"]);
    }

    [Fact]
    public void HasSecretAttributes_And_NeedsStoredEntity()
    {
        var tenant = SecretTestModel.TenantId;
        Assert.True(_normalizer.HasSecretAttributes(_model.Cache, tenant, _model.Config));
        Assert.True(_normalizer.HasSecretAttributes(_model.Cache, tenant, _model.Wrapper));
        Assert.False(_normalizer.HasSecretAttributes(_model.Cache, tenant, _model.Plain));
        Assert.False(_normalizer.HasSecretAttributes(_model.Cache, tenant, _model.Settings));

        var updateTopLevelOnly = _model.NewConfig();
        updateTopLevelOnly.SetAttributeRawValue("Password", "");
        var updateRecords = _model.NewConfig();
        updateRecords.SetAttributeRawValue("Credentials", new List<RtRecord>());

        Assert.False(_normalizer.NeedsStoredEntity(_model.Cache, tenant, _model.Config, updateTopLevelOnly,
            SecretWriteOperation.Update));
        Assert.True(_normalizer.NeedsStoredEntity(_model.Cache, tenant, _model.Config, updateRecords,
            SecretWriteOperation.Update));
        Assert.True(_normalizer.NeedsStoredEntity(_model.Cache, tenant, _model.Config, updateTopLevelOnly,
            SecretWriteOperation.Replace));
        Assert.False(_normalizer.NeedsStoredEntity(_model.Cache, tenant, _model.Config, updateRecords,
            SecretWriteOperation.Insert));
        Assert.False(_normalizer.NeedsStoredEntity(_model.Cache, tenant, _model.Plain, updateRecords,
            SecretWriteOperation.Replace));
    }

    [Fact]
    public void NormalizeAttributeValue_SingleSlot()
    {
        var tenant = SecretTestModel.TenantId;
        var password = _model.Config.AllAttributesByName["Password"];
        var credentials = _model.Config.AllAttributesByName["Credentials"];
        var name = _model.Config.AllAttributesByName["Name"];

        Assert.Null(_normalizer.NormalizeAttributeValue(_model.Cache, tenant, password, "", SecretValueOrigin.Storage));
        Assert.Null(_normalizer.NormalizeAttributeValue(_model.Cache, tenant, password, "TODO_SET_X", SecretValueOrigin.Storage));
        Assert.Null(_normalizer.NormalizeAttributeValue(_model.Cache, tenant, password, null, SecretValueOrigin.Storage));
        Assert.Equal(SecretTestModel.V1VectorPlaintext, Decrypt(_normalizer.NormalizeAttributeValue(_model.Cache, tenant,
            password, SecretTestModel.V1Vector, SecretValueOrigin.Storage)));
        Assert.Equal("Bob", _normalizer.NormalizeAttributeValue(_model.Cache, tenant, name, "Bob", SecretValueOrigin.Storage));

        var list = new List<RtRecord> { _model.CredentialRecord("a", "<P>"), _model.CredentialRecord("b", "plain-b") };
        var normalised = (IEnumerable<RtRecord>)_normalizer.NormalizeAttributeValue(_model.Cache, tenant, credentials,
            list, SecretValueOrigin.Storage)!;
        var elements = normalised.ToList();
        Assert.Null(elements[0].Attributes["Value"]);
        Assert.Equal("plain-b", Decrypt(elements[1].Attributes["Value"]));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("<X>", true)]
    [InlineData("TODO_SET_X", true)]
    [InlineData("value", false)]
    [InlineData(" ", false)]
    public void IsNotSetValue(string? value, bool expected)
    {
        Assert.Equal(expected, SecretWriteNormalizer.IsNotSetValue(value));
        if (value != null)
        {
            Assert.Equal(expected, SecretWriteNormalizer.IsNotSetValue(RtSecretValue.Pending(value)));
            Assert.Equal(expected, SecretWriteNormalizer.IsNotSetValue(RtSecretValue.LegacyPlaintext(value)));
        }
    }

    [Fact]
    public void AttributeHasSecrets()
    {
        var tenant = SecretTestModel.TenantId;
        Assert.True(_normalizer.AttributeHasSecrets(_model.Cache, tenant, _model.Config.AllAttributesByName["Password"]));
        Assert.True(_normalizer.AttributeHasSecrets(_model.Cache, tenant, _model.Config.AllAttributesByName["Wrappers"]));
        Assert.False(_normalizer.AttributeHasSecrets(_model.Cache, tenant, _model.Config.AllAttributesByName["Settings"]));
        Assert.False(_normalizer.AttributeHasSecrets(_model.Cache, tenant, _model.Config.AllAttributesByName["Name"]));
    }

    #endregion
}
