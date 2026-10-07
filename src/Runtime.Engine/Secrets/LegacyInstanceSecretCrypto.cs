using System.Security.Cryptography;
using System.Text;
using Meshmakers.Octo.Runtime.Contracts.Secrets;

namespace Meshmakers.Octo.Runtime.Engine.Secrets;

/// <summary>
///     The legacy <c>enc:v1:</c> format of octo-sdk <c>InstanceSecretCrypto</c>
///     (<c>Sdk.Common/Encryption/InstanceSecretCrypto.cs</c>), replicated byte-for-byte so the engine
///     can read existing values without referencing the SDK: AES-256-GCM, no associated data,
///     <c>enc:v1:</c> + standard base64 of <c>nonce(12) ‖ tag(16) ‖ ciphertext</c>.
/// </summary>
/// <remarks>
///     <see cref="Encrypt" /> exists for tests and test-vector generation only; the engine never
///     writes <c>enc:v1</c>.
/// </remarks>
internal static class LegacyInstanceSecretCrypto
{
    internal const int KeyLength = 32;

    internal static string Encrypt(byte[] key, string plaintext)
    {
        ValidateKey(key);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(SecretEnvelope.NonceLength);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[SecretEnvelope.TagLength];

        using var aes = new AesGcm(key, SecretEnvelope.TagLength);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var combined = new byte[SecretEnvelope.NonceLength + SecretEnvelope.TagLength + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, SecretEnvelope.NonceLength);
        Buffer.BlockCopy(tag, 0, combined, SecretEnvelope.NonceLength, SecretEnvelope.TagLength);
        Buffer.BlockCopy(ciphertext, 0, combined, SecretEnvelope.NonceLength + SecretEnvelope.TagLength,
            ciphertext.Length);

        return SecretEnvelope.PrefixV1 + Convert.ToBase64String(combined);
    }

    internal static string Decrypt(byte[] key, string envelope)
    {
        ValidateKey(key);
        if (!SecretEnvelope.TryParse(envelope, out var info, out _, out var combined) || info.Version != 1)
        {
            throw new CryptographicException("The value is not a valid 'enc:v1' envelope.");
        }

        var nonce = combined.AsSpan(0, SecretEnvelope.NonceLength);
        var tag = combined.AsSpan(SecretEnvelope.NonceLength, SecretEnvelope.TagLength);
        var cipher = combined.AsSpan(SecretEnvelope.NonceLength + SecretEnvelope.TagLength);
        var plaintextBytes = new byte[cipher.Length];

        using var aes = new AesGcm(key, SecretEnvelope.TagLength);
        aes.Decrypt(nonce, cipher, tag, plaintextBytes);

        return Encoding.UTF8.GetString(plaintextBytes);
    }

    private static void ValidateKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeyLength)
        {
            throw new ArgumentException($"Key must be {KeyLength} bytes (AES-256); got {key.Length}.", nameof(key));
        }
    }
}
