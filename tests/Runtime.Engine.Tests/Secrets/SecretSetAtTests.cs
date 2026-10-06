using System.Text.Json;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Secrets;
using Meshmakers.Octo.Runtime.Engine.Secrets;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Secrets;

/// <summary>
///     AB#5532 (handover §7, Q2): "set at" of protected values - set when new input is protected, kept by
///     carry-over / re-protect, null for converted legacy values; never in the marker.
/// </summary>
public class SecretSetAtTests
{
    private static readonly DateTime Earlier = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    private readonly SecretTestModel _model = new();
    private readonly SecretAttributeProtector _protector = SecretTestModel.CreateProtector();

    [Fact]
    public void Protect_SetsSetAtToNow()
    {
        var before = DateTime.UtcNow;
        var value = _protector.Protect("x");

        Assert.NotNull(value.SetAt);
        Assert.Equal(DateTimeKind.Utc, value.SetAt!.Value.Kind);
        Assert.InRange(value.SetAt.Value, before, DateTime.UtcNow);
    }

    [Fact]
    public void Protected_WithSetAt_NormalisesToUtc_AndIsNotPartOfEquality()
    {
        var envelope = _protector.Protect("x").Envelope!;
        var unspecified = RtSecretValue.Protected(envelope, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Unspecified));

        Assert.Equal(Earlier, unspecified.SetAt);
        Assert.Equal(DateTimeKind.Utc, unspecified.SetAt!.Value.Kind);
        Assert.Null(RtSecretValue.Protected(envelope).SetAt);
        Assert.Equal(RtSecretValue.Protected(envelope), unspecified);
        Assert.Equal(RtSecretValue.Protected(envelope).GetHashCode(), unspecified.GetHashCode());
        Assert.Null(unspecified.WithSetAt(null).SetAt);
        Assert.Throws<InvalidOperationException>(() => RtSecretValue.Pending("x").WithSetAt(Earlier));
    }

    [Fact]
    public void Marker_DoesNotCarrySetAt()
    {
        var value = _protector.Protect("x");

        Assert.Equal("{\"isSet\":true}", JsonSerializer.Serialize(value));
    }

    [Fact]
    public void Reprotect_KeepsSetAtOfProtectedValues_AndLegacyGetsNone()
    {
        var k2 = SecretTestModel.CreateProtector(activeKeyId: "k2").Protect("x").WithSetAt(Earlier);

        var rotated = _protector.Reprotect(k2);
        var converted = _protector.Reprotect(RtSecretValue.LegacyPlaintext("legacy"));
        var convertedV1 = _protector.Reprotect(RtSecretValue.LegacyPlaintext(SecretTestModel.V1Vector));

        Assert.Equal("k1", rotated.KeyId);
        Assert.Equal(Earlier, rotated.SetAt);
        Assert.Null(converted.SetAt);
        Assert.Null(convertedV1.SetAt);
        Assert.NotNull(_protector.Reprotect(RtSecretValue.Pending("new")).SetAt);
    }

    [Fact]
    public void WritePath_NewInputGetsNow_CarryOverKeeps_LegacyConversionHasNone()
    {
        var normalizer = new SecretWriteNormalizer(_protector);
        var stored = _model.NewConfig();
        stored.SetAttributeRawValue("ApiKey", _protector.Protect("api").WithSetAt(Earlier));
        stored.SetAttributeRawValue("Password", RtSecretValue.LegacyPlaintext("legacy-pw"));
        var incoming = _model.NewConfig(stored.RtId);
        incoming.SetAttributeRawValue("Credentials", new List<RtRecord> { _model.CredentialRecord("a", "new") });

        var before = DateTime.UtcNow;
        normalizer.Normalize(_model.Cache, SecretTestModel.TenantId, _model.Config, incoming,
            SecretWriteOperation.Replace, stored);

        Assert.Equal(Earlier, ((RtSecretValue)incoming.Attributes["ApiKey"]!).SetAt);
        Assert.Null(((RtSecretValue)incoming.Attributes["Password"]!).SetAt);
        var element = ((IEnumerable<RtRecord>)incoming.Attributes["Credentials"]!).Single();
        Assert.InRange(((RtSecretValue)element.Attributes["Value"]!).SetAt!.Value, before, DateTime.UtcNow);
    }

    [Fact]
    public void Describe_ReportsFormKeyIdAndSetAt()
    {
        var protectedValue = _protector.Protect("x").WithSetAt(Earlier);
        var unknown = RtSecretValue.Protected("enc:v2:k9:AAECAwQFBgcICQoLce30XInwk1La5ADQECWjFW2r_6nUXsA", Earlier);

        Assert.Equal(new SecretReadInfo(SecretValueState.Set, SecretStorageForm.EncV2, "k1", Earlier),
            _protector.DescribeSecret(protectedValue));
        var missing = _protector.DescribeSecret(unknown);
        Assert.Equal(SecretStorageForm.KeyMissing, missing.Form);
        Assert.True(missing.KeyMissing);
        Assert.False(missing.IsSet);
        Assert.Equal("k9", missing.KeyId);
        Assert.Equal(Earlier, missing.SetAt);
        Assert.Equal(SecretStorageForm.EncV1, _protector.DescribeSecret(RtSecretValue.LegacyPlaintext(SecretTestModel.V1Vector)).Form);
        Assert.Equal(SecretStorageForm.Plaintext, _protector.DescribeSecret(RtSecretValue.LegacyPlaintext("clear")).Form);
        Assert.Equal(SecretStorageForm.NotSet, _protector.DescribeSecret(RtSecretValue.LegacyPlaintext("TODO_SET_X")).Form);
        Assert.Equal(SecretStorageForm.Corrupt,
            _protector.DescribeSecret(RtSecretValue.LegacyPlaintext(protectedValue.Envelope!)).Form);
        Assert.Equal(SecretStorageForm.NotSet, _protector.DescribeSecret(null).Form);

        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("ApiKey", protectedValue);
        Assert.Equal(Earlier, entity.DescribeSecret("ApiKey", _protector).SetAt);
    }
}
