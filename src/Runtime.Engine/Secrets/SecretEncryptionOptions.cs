namespace Meshmakers.Octo.Runtime.Engine.Secrets;

/// <summary>
///     Key ring for <c>Secret</c> attributes (AB#5528, concept §3.5), bound from the configuration
///     section <see cref="SectionName" /> by <c>AddRuntimeEngine()</c>. Delivered per cluster as
///     environment variables (AB#5536): <c>OCTO_SECRETENCRYPTION__KEYS__k1</c>,
///     <c>OCTO_SECRETENCRYPTION__ACTIVEKEYID</c>, <c>OCTO_SECRETENCRYPTION__LEGACYV1KEY</c>.
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
}
