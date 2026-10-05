using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using Meshmakers.Octo.Runtime.Contracts.Secrets;
using Meshmakers.Octo.Runtime.Engine.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Secrets;

/// <summary>
///     AB#5531: AES-256-GCM envelope <c>enc:v2:&lt;kid&gt;:</c>, legacy <c>enc:v1:</c>, key ring handling.
///     The fixed vectors were produced independently (Python <c>cryptography</c> AESGCM) from the
///     concept's format definition and the octo-sdk <c>InstanceSecretCrypto</c> layout.
/// </summary>
public class SecretAttributeProtectorTests
{
    // Key 0x00..0x1F (base64) - enc:v1 vector key.
    private const string V1Key = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    // enc:v1 of "Pässwort-v1!" with nonce A0..AB, no AAD.
    private const string V1Vector = "enc:v1:oKGio6SlpqeoqaqrOxh7H702oGiyoSGkNg1vJ7bb2F42vG3NFkjx4iY=";

    // Key 0x42 x 32 (base64) as k1.
    private const string K1 = "QkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkI=";

    // enc:v2 of "hunter2" with k1, nonce 00..0B, AAD "enc:v2:k1:".
    private const string V2Vector = "enc:v2:k1:AAECAwQFBgcICQoLce30XInwk1La5ADQECWjFW2r_6nUXsA";

    private static readonly string K2 = Convert.ToBase64String(Enumerable.Range(100, 32).Select(i => (byte)i).ToArray());

    private static SecretAttributeProtector Create(Action<SecretEncryptionOptions>? configure = null)
    {
        var options = new SecretEncryptionOptions
        {
            Keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["k1"] = K1, ["k2"] = K2 },
            ActiveKeyId = "k1",
            LegacyV1Key = V1Key
        };
        configure?.Invoke(options);
        return new SecretAttributeProtector(Options.Create(options), NullLogger<SecretAttributeProtector>.Instance);
    }

    [Theory]
    [InlineData("hunter2")]
    [InlineData("Pässwort mit Umlauten & <Zeichen>")]
    [InlineData(" leading and trailing whitespace ")]
    [InlineData("enc:looks-like-an-envelope")]
    public void ProtectThenUnprotect_RoundTrips(string plaintext)
    {
        var protector = Create();

        var value = protector.Protect(plaintext);

        Assert.True(value.IsProtected);
        Assert.StartsWith("enc:v2:k1:", value.Envelope);
        Assert.DoesNotContain(plaintext.Trim(), value.Envelope!);
        Assert.Equal("k1", value.KeyId);
        Assert.Equal(plaintext, protector.Unprotect(value));
        Assert.Equal(plaintext, protector.Unprotect(value.Envelope!));
    }

    [Fact]
    public void Protect_UsesRandomNonce()
    {
        var protector = Create();

        Assert.NotEqual(protector.Protect("same").Envelope, protector.Protect("same").Envelope);
    }

    [Fact]
    public void Unprotect_KnownV2Vector()
    {
        Assert.Equal("hunter2", Create().Unprotect(V2Vector));
    }

    [Fact]
    public void Unprotect_KnownV1Vector_FromLegacyFormat()
    {
        var protector = Create();

        Assert.Equal("Pässwort-v1!", protector.Unprotect(V1Vector));
        Assert.Equal("Pässwort-v1!", protector.Unprotect(RtSecretValue.LegacyPlaintext(V1Vector)));
    }

    [Fact]
    public void CopiedV1Logic_RoundTrips()
    {
        var key = Convert.FromBase64String(V1Key);
        var envelope = LegacyInstanceSecretCrypto.Encrypt(key, "legacy");

        Assert.StartsWith("enc:v1:", envelope);
        Assert.Equal("legacy", Create().Unprotect(envelope));
    }

    [Fact]
    public void Unprotect_LegacyPlaintext_IsReturnedAndCounted()
    {
        var protector = Create();
        long plaintextReads = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == SecretDiagnostics.MeterName && instrument.Name == "octo.secrets.plaintext_reads")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => Interlocked.Add(ref plaintextReads, measurement));
        listener.Start();

        var result = protector.Unprotect(RtSecretValue.LegacyPlaintext("old-clear-text"),
            new SecretAccessContext("t1", "System.Communication/Sftp", "Password"));

        Assert.Equal("old-clear-text", result);
        Assert.True(Interlocked.Read(ref plaintextReads) >= 1);
    }

    [Fact]
    public void Unprotect_CountsDecryptsWithTags()
    {
        var protector = Create();
        var tags = new List<KeyValuePair<string, object?>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == SecretDiagnostics.MeterName && instrument.Name == "octo.secrets.decrypt")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, measurementTags, _) =>
        {
            foreach (var tag in measurementTags)
            {
                if (tag.Value as string == "MarkerAttr")
                {
                    lock (tags)
                    {
                        tags.AddRange(measurementTags.ToArray());
                    }
                }
            }
        });
        listener.Start();

        protector.Unprotect(V2Vector, new SecretAccessContext("tenantA", "Model/Type", "MarkerAttr", "svc"));

        lock (tags)
        {
            Assert.Contains(tags, t => t is { Key: "tenant", Value: "tenantA" });
            Assert.Contains(tags, t => t is { Key: "ckType", Value: "Model/Type" });
            Assert.Contains(tags, t => t is { Key: "service", Value: "svc" });
            Assert.Contains(tags, t => t is { Key: "form", Value: "enc_v2" });
            Assert.DoesNotContain(tags, t => t.Value as string == "hunter2");
        }
    }

    [Fact]
    public void Unprotect_TamperedTag_Throws()
    {
        var protector = Create();
        var envelope = protector.Protect("secret").Envelope!;
        var payload = System.Buffers.Text.Base64Url.DecodeFromChars(envelope.AsSpan("enc:v2:k1:".Length));
        payload[SecretEnvelope.NonceLength] ^= 0x01;
        var tampered = "enc:v2:k1:" + System.Buffers.Text.Base64Url.EncodeToString(payload);

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(tampered));
    }

    [Fact]
    public void Unprotect_TamperedCiphertext_Throws()
    {
        var protector = Create();
        var envelope = protector.Protect("secret").Envelope!;
        var payload = System.Buffers.Text.Base64Url.DecodeFromChars(envelope.AsSpan("enc:v2:k1:".Length));
        payload[^1] ^= 0x80;
        var tampered = "enc:v2:k1:" + System.Buffers.Text.Base64Url.EncodeToString(payload);

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(tampered));
    }

    [Fact]
    public void Unprotect_HeaderSwappedToOtherKnownKid_Throws()
    {
        // k2 exists, but the AAD and the key no longer match the ciphertext.
        var protector = Create();
        var envelope = protector.Protect("secret").Envelope!;
        var swapped = "enc:v2:k2:" + envelope["enc:v2:k1:".Length..];

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(swapped));
    }

    [Fact]
    public void Unprotect_SameKeyUnderOtherKid_StillThrows_BecauseOfAad()
    {
        // Same key material registered as k3: only the header differs - the AAD binds the kid.
        var protector = Create(o => o.Keys["k3"] = K1);

        Assert.ThrowsAny<CryptographicException>(() =>
            protector.Unprotect("enc:v2:k3:" + V2Vector["enc:v2:k1:".Length..]));
    }

    [Fact]
    public void Unprotect_UnknownKid_ThrowsClearError()
    {
        var protector = Create(o => o.Keys.Remove("k1"));

        var exception = Assert.Throws<UnknownSecretKeyIdException>(() => protector.Unprotect(V2Vector));

        Assert.Equal("k1", exception.KeyId);
        Assert.Contains("'k1'", exception.Message);
        Assert.IsAssignableFrom<CryptographicException>(exception);
    }

    [Fact]
    public void KeyIds_AreCaseInsensitive_HeaderKeepsConfiguredKid()
    {
        var protector = Create(o =>
        {
            o.Keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["K1"] = K1 };
            o.ActiveKeyId = "k1";
        });

        var value = protector.Protect("x");

        Assert.StartsWith("enc:v2:k1:", value.Envelope);
        Assert.Equal("hunter2", protector.Unprotect(V2Vector));
    }

    [Fact]
    public void NotConfigured_ProtectAndUnprotectThrow_PlaintextStillReadable()
    {
        var protector = new SecretAttributeProtector(Options.Create(new SecretEncryptionOptions()),
            NullLogger<SecretAttributeProtector>.Instance);

        Assert.False(protector.IsConfigured);
        Assert.Null(protector.ActiveKeyId);
        Assert.Throws<SecretEncryptionNotConfiguredException>(() => protector.Protect("x"));
        Assert.Throws<SecretEncryptionNotConfiguredException>(() => protector.Unprotect(V2Vector));
        Assert.Throws<SecretEncryptionNotConfiguredException>(() => protector.Unprotect(V1Vector));
        Assert.Equal("clear", protector.Unprotect("clear"));
    }

    [Fact]
    public void InvalidKeyMaterial_IsReportedWithoutCrashing()
    {
        var protector = Create(o =>
        {
            o.Keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["k1"] = "dG9vLXNob3J0" };
        });

        Assert.False(protector.IsConfigured);
        var exception = Assert.Throws<SecretEncryptionNotConfiguredException>(() => protector.Protect("x"));
        Assert.Contains("32-byte", exception.Message);
        Assert.DoesNotContain("dG9vLXNob3J0", exception.Message);
    }

    [Fact]
    public void NeedsReprotect_AndReprotect()
    {
        var protector = Create();
        var current = protector.Protect("value");
        var withK2 = Create(o => o.ActiveKeyId = "k2").Protect("value");

        Assert.False(protector.NeedsReprotect(current));
        Assert.True(protector.NeedsReprotect(withK2));
        Assert.True(protector.NeedsReprotect(RtSecretValue.LegacyPlaintext("clear")));
        Assert.True(protector.NeedsReprotect(RtSecretValue.LegacyPlaintext(V1Vector)));
        Assert.True(protector.NeedsReprotect(RtSecretValue.Pending("p")));

        Assert.Same(current, protector.Reprotect(current));
        var reprotected = protector.Reprotect(withK2);
        Assert.Equal("k1", reprotected.KeyId);
        Assert.Equal("value", protector.Unprotect(reprotected));
        Assert.Equal("Pässwort-v1!", protector.Unprotect(protector.Reprotect(RtSecretValue.LegacyPlaintext(V1Vector))));
    }

    [Theory]
    [InlineData(V2Vector, true, 2, "k1")]
    [InlineData(V1Vector, true, 1, null)]
    [InlineData("enc:v2:k1:", false, 0, null)]
    [InlineData("enc:v2:k1:AAAA", false, 0, null)] // payload too short
    [InlineData("enc:v2:k:1:AAECAwQFBgcICQoLce30XInwk1La5ADQECWjFW2r_6nUXsA", false, 0, null)]
    [InlineData("enc:v2:k1:AAECAwQFBgcICQoLce30XInwk1La5ADQECWjFW2r+6nUXsA", false, 0, null)] // '+' is not base64url
    [InlineData("enc:v2:k1:AAECAwQFBgcICQoLce30XInwk1La5ADQECWjFW2r_6nUXsA=", false, 0, null)] // padding
    [InlineData("enc:v3:k1:AAECAwQFBgcICQoLce30XInwk1La5ADQECWjFW2r_6nUXsA", false, 0, null)]
    [InlineData("enc:my-password", false, 0, null)]
    [InlineData("enc:v1:not base64!", false, 0, null)]
    [InlineData("enc:v1:AAAA", false, 0, null)]
    [InlineData("hunter2", false, 0, null)]
    [InlineData("", false, 0, null)]
    public void EnvelopeParsing_IsStrict(string value, bool expected, int version, string? keyId)
    {
        var protector = Create();

        Assert.Equal(expected, protector.IsProtectedEnvelope(value));
        Assert.Equal(expected, protector.TryParseEnvelope(value, out var info));
        if (expected)
        {
            Assert.Equal(version, info.Version);
            Assert.Equal(keyId, info.KeyId);
        }
    }

    [Fact]
    public void PlaintextStartingWithEnc_IsNotMistakenForAnEnvelope()
    {
        Assert.Equal("enc:my-password", Create().Unprotect("enc:my-password"));
    }

    [Fact]
    public void AddRuntimeEngine_BindsSecretEncryptionSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecretEncryption:Keys:k1"] = K1,
                ["SecretEncryption:ActiveKeyId"] = "k1",
                ["SecretEncryption:LegacyV1Key"] = V1Key
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddRuntimeEngine();
        using var provider = services.BuildServiceProvider();

        var protector = provider.GetRequiredService<ISecretAttributeProtector>();
        var options = provider.GetRequiredService<IOptions<SecretEncryptionOptions>>().Value;

        Assert.True(protector.IsConfigured);
        Assert.Equal("hunter2", protector.Unprotect(V2Vector));
        Assert.Equal("Pässwort-v1!", protector.Unprotect(V1Vector));
        Assert.True(options.Keys.ContainsKey("K1"));
    }

    [Fact]
    public void AddRuntimeEngine_WithoutConfiguration_StartsUnconfigured()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRuntimeEngine();
        using var provider = services.BuildServiceProvider();

        var protector = provider.GetRequiredService<ISecretAttributeProtector>();

        Assert.False(protector.IsConfigured);
    }
}
