using System.Buffers.Text;
using System.Text.RegularExpressions;

namespace Meshmakers.Octo.Runtime.Contracts.Secrets;

/// <summary>
///     Result of parsing a secret envelope.
/// </summary>
/// <param name="Version">Envelope version: 1 (<c>enc:v1:</c>, no key id) or 2 (<c>enc:v2:&lt;kid&gt;:</c>)</param>
/// <param name="KeyId">Key id of a version 2 envelope; <c>null</c> for version 1</param>
public readonly record struct SecretEnvelopeInfo(int Version, string? KeyId);

/// <summary>
///     Strict structural parsing of secret envelopes (AB#5528, concept §3.4). No key material is
///     needed; a successful parse does not mean the value decrypts.
/// </summary>
/// <remarks>
///     <para>
///         <c>enc:v2:&lt;kid&gt;:&lt;base64url(nonce[12] ‖ tag[16] ‖ ciphertext)&gt;</c> - the kid is
///         1-32 characters of <c>[A-Za-z0-9_-]</c>, the payload is unpadded base64url and decodes to
///         at least 28 bytes.
///     </para>
///     <para>
///         <c>enc:v1:&lt;base64(nonce[12] ‖ tag[16] ‖ ciphertext)&gt;</c> - the legacy
///         <c>InstanceSecretCrypto</c> format of octo-sdk, standard base64 with padding.
///     </para>
///     <para>
///         Anything else is not an envelope - including a clear-text password that happens to start
///         with <c>enc:</c>, which <c>InstanceSecretCrypto.IsEncrypted</c> would have misread.
///     </para>
/// </remarks>
public static class SecretEnvelope
{
    /// <summary>
    ///     Prefix of the current envelope format.
    /// </summary>
    public const string PrefixV2 = "enc:v2:";

    /// <summary>
    ///     Prefix of the legacy envelope format (octo-sdk <c>InstanceSecretCrypto</c>).
    /// </summary>
    public const string PrefixV1 = "enc:v1:";

    /// <summary>
    ///     The version written by <see cref="ISecretAttributeProtector.Protect" />.
    /// </summary>
    public const int CurrentVersion = 2;

    /// <summary>
    ///     AES-GCM nonce length in bytes.
    /// </summary>
    public const int NonceLength = 12;

    /// <summary>
    ///     AES-GCM tag length in bytes.
    /// </summary>
    public const int TagLength = 16;

    /// <summary>
    ///     Maximum length of a key id.
    /// </summary>
    public const int MaxKeyIdLength = 32;

    private static readonly Regex KeyIdRegex = new("^[A-Za-z0-9_-]{1,32}$", RegexOptions.CultureInvariant);

    /// <summary>
    ///     True when <paramref name="keyId" /> is a valid key id (1-32 characters of
    ///     <c>[A-Za-z0-9_-]</c>; in particular no <c>:</c>).
    /// </summary>
    public static bool IsValidKeyId(string? keyId)
    {
        return keyId != null && KeyIdRegex.IsMatch(keyId);
    }

    /// <summary>
    ///     Builds the header of a version 2 envelope, which is also the AES-GCM associated data.
    /// </summary>
    public static string BuildHeaderV2(string keyId)
    {
        if (!IsValidKeyId(keyId))
        {
            throw new ArgumentException($"'{keyId}' is not a valid secret key id.", nameof(keyId));
        }

        return PrefixV2 + keyId + ":";
    }

    /// <summary>
    ///     True when <paramref name="value" /> is a structurally valid v1 or v2 envelope.
    /// </summary>
    public static bool IsEnvelope(string? value)
    {
        return TryParse(value, out _);
    }

    /// <summary>
    ///     Parses an envelope strictly.
    /// </summary>
    /// <param name="value">The candidate value</param>
    /// <param name="info">Version and key id on success</param>
    /// <returns>True for a structurally valid envelope</returns>
    public static bool TryParse(string? value, out SecretEnvelopeInfo info)
    {
        return TryParse(value, out info, out _, out _);
    }

    /// <summary>
    ///     Parses an envelope strictly and returns its decoded payload and header.
    /// </summary>
    /// <param name="value">The candidate value</param>
    /// <param name="info">Version and key id on success</param>
    /// <param name="header">The header (associated data for v2; the v1 prefix for v1)</param>
    /// <param name="payload">The decoded <c>nonce ‖ tag ‖ ciphertext</c> bytes</param>
    /// <returns>True for a structurally valid envelope</returns>
    public static bool TryParse(string? value, out SecretEnvelopeInfo info, out string header, out byte[] payload)
    {
        info = default;
        header = string.Empty;
        payload = [];
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (value.StartsWith(PrefixV2, StringComparison.Ordinal))
        {
            var separator = value.IndexOf(':', PrefixV2.Length);
            if (separator < 0)
            {
                return false;
            }

            var keyId = value.Substring(PrefixV2.Length, separator - PrefixV2.Length);
            if (!IsValidKeyId(keyId))
            {
                return false;
            }

            var encoded = value[(separator + 1)..];
            if (!IsBase64UrlUnpadded(encoded))
            {
                return false;
            }

            byte[] decoded;
            try
            {
                decoded = Base64Url.DecodeFromChars(encoded);
            }
            catch (FormatException)
            {
                return false;
            }

            if (decoded.Length < NonceLength + TagLength)
            {
                return false;
            }

            info = new SecretEnvelopeInfo(2, keyId);
            header = value[..(separator + 1)];
            payload = decoded;
            return true;
        }

        if (value.StartsWith(PrefixV1, StringComparison.Ordinal))
        {
            var encoded = value[PrefixV1.Length..];
            if (encoded.Length == 0 || encoded.Length % 4 != 0)
            {
                return false;
            }

            byte[] decoded;
            try
            {
                decoded = Convert.FromBase64String(encoded);
            }
            catch (FormatException)
            {
                return false;
            }

            if (decoded.Length < NonceLength + TagLength)
            {
                return false;
            }

            info = new SecretEnvelopeInfo(1, null);
            header = PrefixV1;
            payload = decoded;
            return true;
        }

        return false;
    }

    private static bool IsBase64UrlUnpadded(string encoded)
    {
        if (encoded.Length == 0 || encoded.Length % 4 == 1)
        {
            return false;
        }

        foreach (var c in encoded)
        {
            if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_'))
            {
                return false;
            }
        }

        return true;
    }
}
