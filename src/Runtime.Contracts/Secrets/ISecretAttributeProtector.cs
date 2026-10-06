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
    ///     True when strict mode is on (configuration <c>SecretEncryption:StrictMode</c>, concept
    ///     decision 10, §5.2 phase 5): legacy clear text is no longer readable through
    ///     <see cref="Unprotect(RtSecretValue, SecretAccessContext?)" /> /
    ///     <see cref="Unprotect(string, SecretAccessContext?)" />, which then throw
    ///     <see cref="LegacyPlaintextSecretRejectedException" />. <see cref="Reprotect" /> still converts it.
    ///     Implementations without strict mode return <c>false</c>.
    /// </summary>
    bool IsStrictMode => false;

    /// <summary>
    ///     True when <paramref name="keyId" /> names a key of the key ring (case-insensitive), i.e. an
    ///     <c>enc:v2</c> envelope with this key id can be decrypted. The sweep (AB#5532) classifies
    ///     values with an unknown key id (decision 5: restored from another environment) with it.
    /// </summary>
    bool IsKnownKeyId(string? keyId);

    /// <summary>
    ///     Encrypts a plaintext with the active key: <c>enc:v2:&lt;kid&gt;:...</c>, AES-256-GCM, random
    ///     nonce, associated data = the ASCII header. The result's <see cref="RtSecretValue.SetAt" /> is the
    ///     current UTC time (new input).
    /// </summary>
    /// <exception cref="SecretEncryptionNotConfiguredException">No active key</exception>
    RtSecretValue Protect(string plaintext);

    /// <summary>
    ///     Returns the plaintext of a secret value: decrypts <c>enc:v2</c> (key ring) and <c>enc:v1</c>
    ///     (legacy key), returns legacy clear text (counted as a plaintext read) and the plaintext of
    ///     a pending value. In strict mode (<see cref="IsStrictMode" />) legacy clear text is rejected.
    ///     A legacy value (a string found in a Secret slot) may be clear text or <c>enc:v1</c>; an
    ///     <c>enc:v2</c> envelope as legacy text is never decrypted (AB#5532: the engine stores <c>enc:v2</c>
    ///     only in the protected form, so such a text was copied there).
    /// </summary>
    /// <exception cref="LegacyPlaintextSecretRejectedException">Strict mode and the value is legacy clear text</exception>
    /// <exception cref="SecretEnvelopeNotAllowedException">A legacy value whose text is an <c>enc:v2</c> envelope</exception>
    /// <exception cref="SecretEncryptionNotConfiguredException">The needed key material is not configured</exception>
    /// <exception cref="UnknownSecretKeyIdException">The envelope's key id is not in the key ring</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The value was tampered with or the key is wrong</exception>
    string Unprotect(RtSecretValue value, SecretAccessContext? context = null);

    /// <summary>
    ///     Returns the plaintext of a stored string: an <c>enc:v2</c> or <c>enc:v1</c> envelope is
    ///     decrypted, anything else is treated as legacy clear text (counted as a plaintext read; rejected
    ///     in strict mode, see <see cref="IsStrictMode" />).
    /// </summary>
    /// <remarks>
    ///     This overload is the explicit envelope entry point (a caller that holds an envelope it trusts,
    ///     e.g. <c>InstanceSecretCrypto</c>) and still decrypts <c>enc:v2</c>. Values read from a Secret
    ///     slot go through <see cref="Unprotect(RtSecretValue, SecretAccessContext?)" />, which refuses an
    ///     <c>enc:v2</c> envelope stored as a legacy string; never pass the text of a
    ///     <see cref="RtSecretValueState.LegacyPlaintext" /> value here.
    /// </remarks>
    /// <exception cref="LegacyPlaintextSecretRejectedException">Strict mode and the value is clear text</exception>
    /// <exception cref="SecretEncryptionNotConfiguredException">The needed key material is not configured</exception>
    /// <exception cref="UnknownSecretKeyIdException">The envelope's key id is not in the key ring</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The value was tampered with or the key is wrong</exception>
    string Unprotect(string storedValue, SecretAccessContext? context = null);

    /// <summary>
    ///     Classifies a value for readers (decisions 2026-10-06, item 2) without decrypting it:
    ///     <see cref="SecretValueState.Set" /> (protected with a known key id, non-empty legacy or pending),
    ///     <see cref="SecretValueState.KeyMissing" /> (protected, key id not in the ring - the ciphertext is
    ///     kept and becomes readable once the key is added) or <see cref="SecretValueState.NotSet" />
    ///     (<c>null</c>, empty, a legacy placeholder, or corrupt - see <see cref="SecretValueStates.IsCorrupt" />).
    ///     APIs map it to <c>isSet = (state == Set)</c> and <c>keyMissing = (state == KeyMissing)</c>.
    /// </summary>
    /// <remarks>
    ///     The default implementation is <see cref="SecretValueStates.GetReadState(RtSecretValue?, Func{string?, bool}?)" />
    ///     with <see cref="IsKnownKeyId" />; the engine implementation additionally logs a warning and counts
    ///     (<c>octo.secrets.unreadable</c>, <c>reason=corrupt</c>) a corrupt value - never the value.
    /// </remarks>
    /// <param name="value">The stored value; <c>null</c> = not set</param>
    /// <param name="context">Where the value comes from (log and counter tags only)</param>
    /// <returns>The read state</returns>
    SecretValueState GetReadState(RtSecretValue? value, SecretAccessContext? context = null)
    {
        return SecretValueStates.GetReadState(value, IsKnownKeyId);
    }

    /// <summary>
    ///     Like <see cref="GetReadState" />, plus storage form, key id and "set at"
    ///     (<see cref="RtSecretValue.SetAt" />) - the data behind GraphQL <c>isSet</c> / <c>keyMissing</c> /
    ///     <c>setAt</c> and the secrets overview. Never decrypts.
    /// </summary>
    /// <param name="value">The stored value; <c>null</c> = not set</param>
    /// <param name="context">Where the value comes from (log and counter tags only)</param>
    /// <returns>The description</returns>
    SecretReadInfo DescribeSecret(RtSecretValue? value, SecretAccessContext? context = null)
    {
        return SecretValueStates.Describe(value, IsKnownKeyId);
    }

    /// <summary>
    ///     Reveal helper for the server-side paths that need the plaintext (controller adapter
    ///     configuration, mesh adapter <c>RevealSecret@1</c>, identity providers, AI services, service-account
    ///     tokens; decisions 2026-10-06, item 2): like <see cref="Unprotect(RtSecretValue, SecretAccessContext?)" />,
    ///     but a value that is stored and cannot be read is treated as NOT SET and returns <c>null</c> instead of
    ///     throwing - an unknown key id (<see cref="UnknownSecretKeyIdException" />), a tampered / wrong-key
    ///     envelope (<see cref="System.Security.Cryptography.CryptographicException" />) and an <c>enc:v2</c>
    ///     envelope stored as a legacy string (<see cref="SecretEnvelopeNotAllowedException" />). <c>null</c>,
    ///     empty values and legacy placeholders return <c>null</c> as well. The stored value is never changed.
    /// </summary>
    /// <remarks>
    ///     Configuration problems still throw: <see cref="SecretEncryptionNotConfiguredException" /> (no keys at
    ///     all / no legacy key on this host - the engine implementation; the default implementation maps an
    ///     unknown key id to <c>null</c> without that distinction) and, in strict mode, <see cref="LegacyPlaintextSecretRejectedException" />.
    ///     The engine implementation logs an error (tenant, CK type, attribute, key id - never the value) and
    ///     counts <c>octo.secrets.unreadable</c> for every value it maps to <c>null</c>. The default
    ///     implementation only maps.
    /// </remarks>
    /// <param name="value">The stored value</param>
    /// <param name="context">Reader (decrypt counter, log)</param>
    /// <returns>The plaintext, or <c>null</c> when not set or unreadable</returns>
    string? RevealOrNull(RtSecretValue? value, SecretAccessContext? context = null)
    {
        if (GetReadState(value, context) != SecretValueState.Set)
        {
            return null;
        }

        try
        {
            return Unprotect(value!, context);
        }
        catch (Exception ex) when (ex is UnknownSecretKeyIdException or SecretEnvelopeNotAllowedException or
                                       System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

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
    ///     unchanged. <see cref="RtSecretValue.SetAt" /> is kept for a protected value, <c>null</c> for a
    ///     converted legacy value and the current time for a pending value. Decrypting for the re-encryption is counted like any other decrypt. Legacy clear
    ///     text is converted in strict mode as well (the strict check is bypassed and the read is counted
    ///     as a plaintext read): the encrypt / reprotect sweep must be able to clear the remainder. A legacy
    ///     value whose text is an <c>enc:v2</c> envelope is refused like in
    ///     <see cref="Unprotect(RtSecretValue, SecretAccessContext?)" />.
    /// </summary>
    /// <exception cref="SecretEnvelopeNotAllowedException">A legacy value whose text is an <c>enc:v2</c> envelope</exception>
    RtSecretValue Reprotect(RtSecretValue value, SecretAccessContext? context = null);
}
