using System.Diagnostics.Metrics;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Secrets;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Engine.Secrets;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Secrets;

/// <summary>
///     Decisions 2026-10-06, item 2: read state (<c>isSet</c> / <c>keyMissing</c>) and the reveal helper that
///     treats unreadable values as not set. Unknown key ids are kept, never cleared.
/// </summary>
public class SecretReadStateTests
{
    // enc:v2 of "hunter2" under a key id that is not in the test key ring.
    private const string UnknownKidEnvelope = "enc:v2:k9:AAECAwQFBgcICQoLce30XInwk1La5ADQECWjFW2r_6nUXsA";

    private readonly SecretAttributeProtector _protector = SecretTestModel.CreateProtector();

    public static TheoryData<string, SecretValueState> LegacyCases => new()
    {
        { "", SecretValueState.NotSet },
        { "TODO_SET_PASSWORD", SecretValueState.NotSet },
        { "<set-after-install>", SecretValueState.NotSet },
        { UnknownKidEnvelope, SecretValueState.NotSet }, // corrupt: enc:v2 stored as a legacy string
        { "clear-text", SecretValueState.Set },
        { SecretTestModel.V1Vector, SecretValueState.Set }
    };

    [Theory]
    [MemberData(nameof(LegacyCases))]
    public void LegacyValues(string stored, SecretValueState expected)
    {
        Assert.Equal(expected, _protector.GetReadState(RtSecretValue.LegacyPlaintext(stored)));
        Assert.Equal(expected, SecretValueStates.GetReadState(RtSecretValue.LegacyPlaintext(stored), ["k1"]));
    }

    [Theory]
    [InlineData("", SecretValueState.NotSet)]
    [InlineData("TODO_SET_PASSWORD", SecretValueState.Set)] // input placeholders are values (item 1)
    [InlineData("<x>", SecretValueState.Set)]
    [InlineData("value", SecretValueState.Set)]
    public void PendingValues(string input, SecretValueState expected)
    {
        Assert.Equal(expected, _protector.GetReadState(RtSecretValue.Pending(input)));
    }

    [Fact]
    public void NullAndProtectedValues()
    {
        Assert.Equal(SecretValueState.NotSet, _protector.GetReadState(null));
        Assert.Equal(SecretValueState.Set, _protector.GetReadState(_protector.Protect("x")));
        Assert.Equal(SecretValueState.KeyMissing, _protector.GetReadState(RtSecretValue.Protected(UnknownKidEnvelope)));
    }

    [Fact]
    public void PureHelper_WithKnownKeyIds_AndWithoutKeyRing()
    {
        var unknown = RtSecretValue.Protected(UnknownKidEnvelope);

        Assert.Equal(SecretValueState.KeyMissing, SecretValueStates.GetReadState(unknown, ["k1", "k2"]));
        Assert.Equal(SecretValueState.Set, SecretValueStates.GetReadState(unknown, ["K9"])); // case-insensitive
        // No key ring: key availability is unknown, a protected value counts as set.
        Assert.Equal(SecretValueState.Set, SecretValueStates.GetReadState(unknown, (Func<string?, bool>?)null));
    }

    [Fact]
    public void EncV1_WithoutLegacyKey_IsKeyMissing()
    {
        var v1 = RtSecretValue.LegacyPlaintext(SecretTestModel.V1Vector);

        // Pure helper: the two-argument overloads assume the legacy key is available.
        Assert.Equal(SecretValueState.Set, SecretValueStates.GetReadState(v1, (Func<string?, bool>?)null));
        Assert.Equal(SecretValueState.KeyMissing, SecretValueStates.GetReadState(v1, null, false));
        Assert.Equal(new SecretReadInfo(SecretValueState.KeyMissing, SecretStorageForm.KeyMissing,
            SecretValueStates.LegacyV1KeyId, null), SecretValueStates.Describe(v1, null, false));
        // Clear text is not affected.
        Assert.Equal(SecretValueState.Set,
            SecretValueStates.GetReadState(RtSecretValue.LegacyPlaintext("clear"), null, false));

        foreach (var protector in new[]
                 {
                     SecretTestModel.CreateProtector(legacyV1Key: false),
                     SecretTestModel.CreateProtector(configured: false)
                 })
        {
            Assert.False(protector.IsLegacyV1KeyConfigured);
            Assert.Equal(SecretValueState.KeyMissing, protector.GetReadState(v1));
            var info = protector.DescribeSecret(v1);
            Assert.Equal((SecretStorageForm.KeyMissing, SecretValueStates.LegacyV1KeyId, false, true),
                (info.Form, info.KeyId, info.IsSet, info.KeyMissing));
            // Revealing stays a configuration error on this host (unchanged).
            Assert.Throws<SecretEncryptionNotConfiguredException>(() => protector.RevealOrNull(v1));
        }

        Assert.True(_protector.IsLegacyV1KeyConfigured);
        Assert.Equal(SecretStorageForm.EncV1, _protector.DescribeSecret(v1).Form);
    }

    [Fact]
    public void WireMarker_HasNoKeyRing_AndFollowsTheSameRules()
    {
        Assert.True(RtSecretValueWireFormat.IsSet(RtSecretValue.Protected(UnknownKidEnvelope)));
        Assert.True(RtSecretValueWireFormat.IsSet(RtSecretValue.Pending("TODO_SET_X")));
        Assert.False(RtSecretValueWireFormat.IsSet(RtSecretValue.Pending("")));
        Assert.False(RtSecretValueWireFormat.IsSet(RtSecretValue.LegacyPlaintext("TODO_SET_X")));
        Assert.True(RtSecretValueWireFormat.IsSet(RtSecretValue.LegacyPlaintext("clear")));
    }

    [Fact]
    public void RevealOrNull_ReturnsThePlaintext_ForReadableValues()
    {
        Assert.Equal("x", _protector.RevealOrNull(_protector.Protect("x")));
        Assert.Equal(SecretTestModel.V1VectorPlaintext,
            _protector.RevealOrNull(RtSecretValue.LegacyPlaintext(SecretTestModel.V1Vector)));
        Assert.Equal("TODO_SET_X", _protector.RevealOrNull(RtSecretValue.Pending("TODO_SET_X")));
        Assert.Null(_protector.RevealOrNull(null));
        Assert.Null(_protector.RevealOrNull(RtSecretValue.LegacyPlaintext("TODO_SET_X")));
    }

    [Fact]
    public void RevealOrNull_UnknownKeyId_IsNull_AndCounted_WithoutTheValue()
    {
        var measurements = new List<(long Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = Listen(measurements, "RevealUnknownKid");

        var plaintext = _protector.RevealOrNull(RtSecretValue.Protected(UnknownKidEnvelope),
            new SecretAccessContext("tenantR", "Model/Type", "RevealUnknownKid"));

        Assert.Null(plaintext);
        // Unprotect keeps its contract.
        Assert.Throws<UnknownSecretKeyIdException>(() => _protector.Unprotect(RtSecretValue.Protected(UnknownKidEnvelope)));
        lock (measurements)
        {
            var measurement = Assert.Single(measurements);
            Assert.Contains(measurement.Tags, t => t is { Key: "reason", Value: "unknown_key_id" });
            Assert.DoesNotContain(measurement.Tags, t => t.Value as string == "hunter2");
        }
    }

    [Fact]
    public void RevealOrNull_TamperedOrCorrupt_IsNull()
    {
        var envelope = _protector.Protect("victim").Envelope!;
        var payload = System.Buffers.Text.Base64Url.DecodeFromChars(envelope.AsSpan("enc:v2:k1:".Length));
        payload[SecretEnvelope.NonceLength] ^= 0x01;
        var tampered = RtSecretValue.Protected("enc:v2:k1:" + System.Buffers.Text.Base64Url.EncodeToString(payload));

        Assert.Null(_protector.RevealOrNull(tampered));
        Assert.Null(_protector.RevealOrNull(RtSecretValue.LegacyPlaintext(envelope))); // copied enc:v2
    }

    [Fact]
    public void RevealOrNull_HostWithoutKeys_StillThrowsNotConfigured()
    {
        var unconfigured = SecretTestModel.CreateProtector(configured: false);

        Assert.Throws<SecretEncryptionNotConfiguredException>(() =>
            unconfigured.RevealOrNull(RtSecretValue.Protected(UnknownKidEnvelope)));
    }

    [Fact]
    public void GetSecretPlaintext_UnknownKeyId_ReturnsNull_AndReadStateIsKeyMissing()
    {
        var model = new SecretTestModel();
        var entity = model.NewConfig();
        entity.SetAttributeRawValue("ApiKey", RtSecretValue.Protected(UnknownKidEnvelope));
        entity.SetAttributeRawValue("Password", _protector.Protect("pw"));

        Assert.Null(entity.GetSecretPlaintext("ApiKey", _protector));
        Assert.Equal("pw", entity.GetSecretPlaintext("Password", _protector));
        Assert.Equal(SecretValueState.KeyMissing, entity.GetSecretReadState("ApiKey", _protector));
        Assert.Equal(SecretValueState.Set, entity.GetSecretReadState("Password", _protector));
        // The ciphertext is kept.
        Assert.Equal(UnknownKidEnvelope, ((RtSecretValue)entity.Attributes["ApiKey"]!).Envelope);
    }

    [Fact]
    public void DefaultInterfaceImplementation_MapsUnknownKeyIdToNull()
    {
        ISecretAttributeProtector minimal = new MinimalProtector(_protector);

        Assert.Equal(SecretValueState.KeyMissing, minimal.GetReadState(RtSecretValue.Protected(UnknownKidEnvelope)));
        Assert.Null(minimal.RevealOrNull(RtSecretValue.Protected(UnknownKidEnvelope)));
        Assert.Equal("x", minimal.RevealOrNull(_protector.Protect("x")));
    }

    private static MeterListener Listen(List<(long, KeyValuePair<string, object?>[])> measurements, string marker)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == SecretDiagnostics.MeterName && instrument.Name == "octo.secrets.unreadable")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var array = tags.ToArray();
            if (array.Any(t => t.Value as string == marker))
            {
                lock (measurements)
                {
                    measurements.Add((value, array));
                }
            }
        });
        listener.Start();
        return listener;
    }

    /// <summary>
    ///     An implementation that relies on the default interface members (like test fakes in other repos).
    /// </summary>
    private sealed class MinimalProtector(ISecretAttributeProtector inner) : ISecretAttributeProtector
    {
        public bool IsConfigured => inner.IsConfigured;
        public string? ActiveKeyId => inner.ActiveKeyId;
        public bool IsKnownKeyId(string? keyId) => inner.IsKnownKeyId(keyId);
        public RtSecretValue Protect(string plaintext) => inner.Protect(plaintext);
        public string Unprotect(RtSecretValue value, SecretAccessContext? context = null) => inner.Unprotect(value, context);
        public string Unprotect(string storedValue, SecretAccessContext? context = null) => inner.Unprotect(storedValue, context);
        public bool IsProtectedEnvelope(string? value) => inner.IsProtectedEnvelope(value);
        public bool TryParseEnvelope(string? value, out SecretEnvelopeInfo info) => inner.TryParseEnvelope(value, out info);
        public bool NeedsReprotect(RtSecretValue value) => inner.NeedsReprotect(value);
        public RtSecretValue Reprotect(RtSecretValue value, SecretAccessContext? context = null) => inner.Reprotect(value, context);
    }
}
