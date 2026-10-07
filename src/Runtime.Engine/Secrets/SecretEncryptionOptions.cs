namespace Meshmakers.Octo.Runtime.Engine.Secrets;

/// <summary>
///     Key ring for <c>Secret</c> attributes (AB#5528, concept §3.5), bound from the configuration
///     section <see cref="SectionName" /> by <c>AddRuntimeEngine()</c>. Delivered per cluster as
///     environment variables (AB#5536): <c>OCTO_SECRETENCRYPTION__KEYS__k1</c>,
///     <c>OCTO_SECRETENCRYPTION__ACTIVEKEYID</c>, <c>OCTO_SECRETENCRYPTION__LEGACYV1KEY</c>,
///     <c>OCTO_SECRETENCRYPTION__STRICTMODE</c>.
/// </summary>
public class SecretEncryptionOptions
{
    /// <summary>
    ///     Configuration section name.
    /// </summary>
    public const string SectionName = "SecretEncryption";

    /// <summary>
    ///     Key ring: key id to base64-encoded 32-byte AES-256 key. Key ids are matched
    ///     case-insensitively (configuration keys are case-insensitive); the envelope header carries
    ///     the active key id exactly as configured in <see cref="ActiveKeyId" />.
    /// </summary>
    public Dictionary<string, string> Keys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Key id used to encrypt new values. Must be a key of <see cref="Keys" />.
    /// </summary>
    public string? ActiveKeyId { get; set; }

    /// <summary>
    ///     Base64-encoded 32-byte key of the legacy <c>enc:v1:</c> format (octo-sdk
    ///     <c>InstanceSecretCrypto</c>, the existing <c>instance_secret_key</c>). Remove once no
    ///     <c>enc:v1</c> value remains.
    /// </summary>
    public string? LegacyV1Key { get; set; }

    /// <summary>
    ///     Strict mode (concept decision 10, §5.2 phase 5; env <c>OCTO_SECRETENCRYPTION__STRICTMODE</c>,
    ///     default <c>false</c>): legacy clear text in a <c>Secret</c> slot is no longer readable.
    ///     <see cref="Meshmakers.Octo.Runtime.Contracts.Secrets.ISecretAttributeProtector.Unprotect(Meshmakers.Octo.Runtime.Contracts.RepositoryEntities.RtSecretValue, Meshmakers.Octo.Runtime.Contracts.Secrets.SecretAccessContext?)" />
    ///     of such a value throws
    ///     <see cref="Meshmakers.Octo.Runtime.Contracts.Secrets.LegacyPlaintextSecretRejectedException" /> and
    ///     increments <c>octo.secrets.strict_mode.rejected_reads</c>. <c>enc:v1</c> stays readable (governed
    ///     by <see cref="LegacyV1Key" />). Re-encryption (<c>Reprotect</c>, the encrypt / reprotect sweep and
    ///     the write path) still converts remaining clear text. Enable it 14 days after the sweep reported
    ///     zero plaintext.
    /// </summary>
    public bool StrictMode { get; set; }
}
