using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.Secrets;

/// <summary>
///     Who reads a secret - tags of the <c>octo.secrets.decrypt</c> counter. Every member is
///     optional; the service falls back to the process' service name.
/// </summary>
/// <param name="TenantId">Tenant of the entity</param>
/// <param name="CkTypeId">CK type of the entity</param>
/// <param name="AttributeName">Name of the Secret attribute</param>
/// <param name="Service">Reading service; defaults to <c>OTEL_SERVICE_NAME</c> or the entry assembly</param>
public sealed record SecretAccessContext(
    string? TenantId = null,
    string? CkTypeId = null,
    string? AttributeName = null,
    string? Service = null);

/// <summary>
///     Encrypts and decrypts values of <c>Secret</c> attributes with the instance key ring
///     (AB#5528, concept §3.4, §3.5, §3.7). Implemented in Runtime.Engine and registered by
///     <c>AddRuntimeEngine()</c> from the configuration section <c>SecretEncryption</c>.
/// </summary>
/// <remarks>
///     <para>
///         Decryption is a privileged operation: every <see cref="Unprotect(RtSecretValue, SecretAccessContext?)" />
///         is counted (<c>octo.secrets.decrypt</c>), values are never logged, and the public APIs
///         never decrypt (architecture tests in asset repo and MCP allowlist the callers).
///     </para>
///     <para>
///         Without configured keys the service starts; <see cref="IsConfigured" /> is false and every
///         operation that needs key material throws <see cref="SecretEncryptionNotConfiguredException" />.
///     </para>
/// </remarks>
public interface ISecretAttributeProtector
{
    /// <summary>
    ///     True when an active key is configured, i.e. <see cref="Protect" /> can work.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    ///     The key id new values are encrypted with; <c>null</c> when not configured.
    /// </summary>
    string? ActiveKeyId { get; }

    /// <summary>
    ///     True when <paramref name="keyId" /> names a key of the key ring (case-insensitive), i.e. an
    ///     <c>enc:v2</c> envelope with this key id can be decrypted. The sweep (AB#5532) classifies
    ///     values with an unknown key id (decision 5: restored from another environment) with it.
    /// </summary>
    bool IsKnownKeyId(string? keyId);

    /// <summary>
    ///     Encrypts a plaintext with the active key: <c>enc:v2:&lt;kid&gt;:...</c>, AES-256-GCM, random
    ///     nonce, associated data = the ASCII header.
    /// </summary>
    /// <exception cref="SecretEncryptionNotConfiguredException">No active key</exception>
    RtSecretValue Protect(string plaintext);

    /// <summary>
    ///     Returns the plaintext of a secret value: decrypts <c>enc:v2</c> (key ring) and <c>enc:v1</c>
    ///     (legacy key), returns legacy clear text (counted as a plaintext read) and the plaintext of
    ///     a pending value.
    /// </summary>
    /// <exception cref="SecretEncryptionNotConfiguredException">The needed key material is not configured</exception>
    /// <exception cref="UnknownSecretKeyIdException">The envelope's key id is not in the key ring</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The value was tampered with or the key is wrong</exception>
    string Unprotect(RtSecretValue value, SecretAccessContext? context = null);

    /// <summary>
    ///     Returns the plaintext of a stored string: an <c>enc:v2</c> or <c>enc:v1</c> envelope is
    ///     decrypted, anything else is treated as legacy clear text (counted as a plaintext read).
    /// </summary>
    /// <exception cref="SecretEncryptionNotConfiguredException">The needed key material is not configured</exception>
    /// <exception cref="UnknownSecretKeyIdException">The envelope's key id is not in the key ring</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The value was tampered with or the key is wrong</exception>
    string Unprotect(string storedValue, SecretAccessContext? context = null);

    /// <summary>
    ///     Strict check: true only for a structurally valid <c>enc:v1</c> or <c>enc:v2</c> envelope
    ///     (see <see cref="SecretEnvelope" />). A plaintext starting with <c>enc:</c> is not one.
    /// </summary>
    bool IsProtectedEnvelope(string? value);

    /// <summary>
    ///     Parses an envelope strictly and returns version and key id.
    /// </summary>
    bool TryParseEnvelope(string? value, out SecretEnvelopeInfo info);

    /// <summary>
    ///     True when the value is not protected with the active key: pending, legacy (clear text or
    ///     <c>enc:v1</c>) or <c>enc:v2</c> with another key id. The re-protect sweep (AB#5532) uses it.
    /// </summary>
    bool NeedsReprotect(RtSecretValue value);

    /// <summary>
    ///     Returns the value protected with the active key; a value that already is, is returned
    ///     unchanged. Decrypting for the re-encryption is counted like any other decrypt.
    /// </summary>
    RtSecretValue Reprotect(RtSecretValue value, SecretAccessContext? context = null);
}
