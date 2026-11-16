using System.Security.Cryptography;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Secrets;
using Meshmakers.Octo.Runtime.Engine.Secrets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Secrets;

/// <summary>
///     A value protected for a purpose opens only for exactly that purpose, and never as the value of a Secret
///     attribute; a Secret attribute value never opens for a purpose.
/// </summary>
public class SecretPurposeBindingTests
{
    private const string FirstKey = "QkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkI=";

    private static readonly string SecondKey =
        Convert.ToBase64String(Enumerable.Range(100, 32).Select(index => (byte)index).ToArray());

    private static readonly SecretPurpose QueuedCallerPurpose = new("queued-caller/tenant-a/0123456789abcdef01234567");

    private static SecretAttributeProtector CreateProtector(string activeKeyId = "k1")
    {
        var options = new SecretEncryptionOptions
        {
            Keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["k1"] = FirstKey,
                ["k2"] = SecondKey
            },
            ActiveKeyId = activeKeyId
        };
        return new SecretAttributeProtector(Options.Create(options), NullLogger<SecretAttributeProtector>.Instance);
    }

    [Fact]
    public void ProtectForPurpose_ThenUnprotectForSamePurpose_RoundTrips()
    {
        var protector = CreateProtector();

        var protectedValue = protector.Protect("{\"payload\":1}", QueuedCallerPurpose);

        Assert.True(protectedValue.IsProtected);
        Assert.StartsWith("enc:v2:k1:", protectedValue.Envelope);
        Assert.Equal("{\"payload\":1}", protector.Unprotect(protectedValue.Envelope!, QueuedCallerPurpose));
    }

    [Fact]
    public void PurposeBoundValue_DoesNotOpenForAnotherPurpose()
    {
        var protector = CreateProtector();
        var protectedValue = protector.Protect("payload", QueuedCallerPurpose);
        var otherPurpose = new SecretPurpose("queued-caller/tenant-a/fedcba9876543210fedcba98");

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(protectedValue.Envelope!, otherPurpose));
    }

    [Fact]
    public void PurposeBoundValue_DoesNotOpenAsSecretAttributeValue()
    {
        var protector = CreateProtector();
        var protectedValue = protector.Protect("payload", QueuedCallerPurpose);

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(protectedValue));
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(protectedValue.Envelope!));
        Assert.Null(protector.RevealOrNull(protectedValue));
    }

    [Fact]
    public void SecretAttributeValue_DoesNotOpenForAPurpose()
    {
        var protector = CreateProtector();
        var attributeValue = protector.Protect("payload");

        Assert.ThrowsAny<CryptographicException>(() =>
            protector.Unprotect(attributeValue.Envelope!, QueuedCallerPurpose));
    }

    [Theory]
    [InlineData("payload")]
    [InlineData("enc:v1:oKGio6SlpqeoqaqrOxh7H702oGiyoSGkNg1vJ7bb2F42vG3NFkjx4iY=")]
    [InlineData("")]
    public void UnprotectForPurpose_RefusesAnythingButAVersion2Envelope(string storedValue)
    {
        var protector = CreateProtector();

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(storedValue, QueuedCallerPurpose));
    }

    [Fact]
    public void PurposeBoundValue_SealedWithFormerActiveKey_OpensAfterRotation()
    {
        var protectedBeforeRotation = CreateProtector("k1").Protect("payload", QueuedCallerPurpose);

        var protectorAfterRotation = CreateProtector("k2");

        Assert.Equal("payload", protectorAfterRotation.Unprotect(protectedBeforeRotation.Envelope!, QueuedCallerPurpose));
        Assert.StartsWith("enc:v2:k2:", protectorAfterRotation.Protect("payload", QueuedCallerPurpose).Envelope);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Purpose_MustNotBeEmpty(string? purposeValue)
    {
        var protector = CreateProtector();
        var emptyPurpose = new SecretPurpose(purposeValue!);

        Assert.ThrowsAny<ArgumentException>(() => protector.Protect("payload", emptyPurpose));
        Assert.ThrowsAny<ArgumentException>(() => protector.Unprotect("enc:v2:k1:AAAA", emptyPurpose));
    }

    [Fact]
    public void Purpose_WithoutConfiguredKey_Throws()
    {
        var options = new SecretEncryptionOptions();
        var protector = new SecretAttributeProtector(Options.Create(options),
            NullLogger<SecretAttributeProtector>.Instance);

        Assert.Throws<SecretEncryptionNotConfiguredException>(() => protector.Protect("payload", QueuedCallerPurpose));
    }

    [Fact]
    public void DefaultInterfaceImplementation_RefusesPurposeBinding()
    {
        ISecretAttributeProtector protectorWithoutPurposeSupport = new ProtectorWithoutPurposeSupport();

        Assert.Throws<NotSupportedException>(() =>
            protectorWithoutPurposeSupport.Protect("payload", QueuedCallerPurpose));
        Assert.Throws<NotSupportedException>(() =>
            protectorWithoutPurposeSupport.Unprotect("enc:v2:k1:AAAA", QueuedCallerPurpose));
    }

    private sealed class ProtectorWithoutPurposeSupport : ISecretAttributeProtector
    {
        public bool IsConfigured => true;
        public string? ActiveKeyId => "k1";
        public bool IsKnownKeyId(string? keyId) => keyId == "k1";
        public RtSecretValue Protect(string plaintext) => throw new InvalidOperationException();
        public string Unprotect(RtSecretValue value, SecretAccessContext? context = null) =>
            throw new InvalidOperationException();
        public string Unprotect(string storedValue, SecretAccessContext? context = null) =>
            throw new InvalidOperationException();
        public bool IsProtectedEnvelope(string? value) => false;
        public bool TryParseEnvelope(string? value, out SecretEnvelopeInfo info)
        {
            info = default;
            return false;
        }
        public bool NeedsReprotect(RtSecretValue value) => false;
        public RtSecretValue Reprotect(RtSecretValue value, SecretAccessContext? context = null) => value;
    }
}
