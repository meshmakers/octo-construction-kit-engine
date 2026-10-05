using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Secrets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Runtime.Engine.Secrets;

/// <summary>
///     AES-256-GCM implementation of <see cref="ISecretAttributeProtector" /> over the key ring in
///     <see cref="SecretEncryptionOptions" /> (AB#5528). Stateless apart from the parsed key ring;
///     registered as a singleton. Never logs a value.
/// </summary>
internal sealed class SecretAttributeProtector : ISecretAttributeProtector
{
    private const string FormV2 = "enc_v2";
    private const string FormV1 = "enc_v1";
    private const string FormPlaintext = "plaintext";
    private const string FormPending = "pending";

    private static readonly string? DefaultServiceName =
        Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") is { Length: > 0 } serviceName
            ? serviceName
            : Assembly.GetEntryAssembly()?.GetName().Name;

    private readonly ILogger<SecretAttributeProtector> _logger;
    private readonly Lazy<KeyRing> _keyRing;
    private readonly ConcurrentDictionary<string, bool> _plaintextWarnings = new(StringComparer.Ordinal);

    public SecretAttributeProtector(IOptions<SecretEncryptionOptions> options, ILogger<SecretAttributeProtector> logger)
    {
        _logger = logger;
        var value = options.Value;
        _keyRing = new Lazy<KeyRing>(() => KeyRing.Create(value), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public bool IsConfigured => _keyRing.Value.ActiveKeyId != null;

    /// <inheritdoc />
    public string? ActiveKeyId => _keyRing.Value.ActiveKeyId;

    /// <inheritdoc />
    public RtSecretValue Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var keyRing = _keyRing.Value;
        if (keyRing.ActiveKeyId == null || keyRing.ActiveKey == null)
        {
            throw new SecretEncryptionNotConfiguredException(
                "Cannot encrypt a secret: no active key is configured (SecretEncryption:ActiveKeyId / " +
                "SecretEncryption:Keys)." + keyRing.ProblemSuffix);
        }

        var header = SecretEnvelope.BuildHeaderV2(keyRing.ActiveKeyId);
        var aad = Encoding.ASCII.GetBytes(header);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

        var combined = new byte[SecretEnvelope.NonceLength + SecretEnvelope.TagLength + plaintextBytes.Length];
        var nonce = combined.AsSpan(0, SecretEnvelope.NonceLength);
        var tag = combined.AsSpan(SecretEnvelope.NonceLength, SecretEnvelope.TagLength);
        var ciphertext = combined.AsSpan(SecretEnvelope.NonceLength + SecretEnvelope.TagLength);
        RandomNumberGenerator.Fill(nonce);

        using (var aes = new AesGcm(keyRing.ActiveKey, SecretEnvelope.TagLength))
        {
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, aad);
        }

        CryptographicOperations.ZeroMemory(plaintextBytes);
        return RtSecretValue.Protected(header + Base64Url.EncodeToString(combined));
    }

    /// <inheritdoc />
    public string Unprotect(RtSecretValue value, SecretAccessContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        switch (value.State)
        {
            case RtSecretValueState.Protected:
                return Decrypt(value.RawValue, context);
            case RtSecretValueState.Pending:
                Count(FormPending, context);
                return value.RawValue;
            default:
                return Unprotect(value.RawValue, context);
        }
    }

    /// <inheritdoc />
    public string Unprotect(string storedValue, SecretAccessContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(storedValue);
        if (SecretEnvelope.IsEnvelope(storedValue))
        {
            return Decrypt(storedValue, context);
        }

        // Legacy clear text: readable during the transition, counted and warned about (without the
        // value) so the sweep can be verified. Strict mode (concept §5.2 phase 5) is a follow-up.
        Count(FormPlaintext, context);
        SecretDiagnostics.PlaintextReads.Add(1, BuildTags(FormPlaintext, context));
        var warningKey = $"{context?.CkTypeId}/{context?.AttributeName}";
        if (_plaintextWarnings.TryAdd(warningKey, true))
        {
            _logger.LogWarning(
                "Secret attribute {AttributeName} of {CkTypeId} (tenant {TenantId}) is still stored as clear text; run the secret encrypt sweep",
                context?.AttributeName, context?.CkTypeId, context?.TenantId);
        }

        return storedValue;
    }

    /// <inheritdoc />
    public bool IsProtectedEnvelope(string? value)
    {
        return SecretEnvelope.IsEnvelope(value);
    }

    /// <inheritdoc />
    public bool TryParseEnvelope(string? value, out SecretEnvelopeInfo info)
    {
        return SecretEnvelope.TryParse(value, out info);
    }

    /// <inheritdoc />
    public bool NeedsReprotect(RtSecretValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.IsProtected)
        {
            return true;
        }

        var activeKeyId = _keyRing.Value.ActiveKeyId;
        return activeKeyId == null || !string.Equals(value.KeyId, activeKeyId, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public RtSecretValue Reprotect(RtSecretValue value, SecretAccessContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!NeedsReprotect(value))
        {
            return value;
        }

        return Protect(Unprotect(value, context));
    }

    private string Decrypt(string envelope, SecretAccessContext? context)
    {
        if (!SecretEnvelope.TryParse(envelope, out var info, out var header, out var payload))
        {
            throw new CryptographicException("The value is not a valid secret envelope.");
        }

        var keyRing = _keyRing.Value;
        if (info.Version == 1)
        {
            if (keyRing.LegacyV1Key == null)
            {
                throw new SecretEncryptionNotConfiguredException(
                    "Cannot decrypt an 'enc:v1' secret: SecretEncryption:LegacyV1Key is not configured." +
                    keyRing.ProblemSuffix);
            }

            var plaintextV1 = LegacyInstanceSecretCrypto.Decrypt(keyRing.LegacyV1Key, envelope);
            Count(FormV1, context);
            return plaintextV1;
        }

        if (keyRing.Keys.Count == 0)
        {
            throw new SecretEncryptionNotConfiguredException(
                "Cannot decrypt a secret: no keys are configured (SecretEncryption:Keys)." + keyRing.ProblemSuffix);
        }

        if (!keyRing.Keys.TryGetValue(info.KeyId!, out var key))
        {
            throw new UnknownSecretKeyIdException(info.KeyId!);
        }

        var nonce = payload.AsSpan(0, SecretEnvelope.NonceLength);
        var tag = payload.AsSpan(SecretEnvelope.NonceLength, SecretEnvelope.TagLength);
        var ciphertext = payload.AsSpan(SecretEnvelope.NonceLength + SecretEnvelope.TagLength);
        var plaintextBytes = new byte[ciphertext.Length];

        using (var aes = new AesGcm(key, SecretEnvelope.TagLength))
        {
            // Associated data = the header exactly as stored: a changed version or key id fails the
            // tag check even when the other key would be in the ring.
            aes.Decrypt(nonce, ciphertext, tag, plaintextBytes, Encoding.ASCII.GetBytes(header));
        }

        var plaintext = Encoding.UTF8.GetString(plaintextBytes);
        CryptographicOperations.ZeroMemory(plaintextBytes);
        Count(FormV2, context);
        return plaintext;
    }

    private static void Count(string form, SecretAccessContext? context)
    {
        SecretDiagnostics.Decrypts.Add(1, BuildTags(form, context));
    }

    private static TagList BuildTags(string form, SecretAccessContext? context)
    {
        var tags = new TagList { { "form", form } };
        if (context?.TenantId != null)
        {
            tags.Add("tenant", context.TenantId);
        }

        if (context?.CkTypeId != null)
        {
            tags.Add("ckType", context.CkTypeId);
        }

        if (context?.AttributeName != null)
        {
            tags.Add("attribute", context.AttributeName);
        }

        var service = context?.Service ?? DefaultServiceName;
        if (service != null)
        {
            tags.Add("service", service);
        }

        return tags;
    }

    /// <summary>
    ///     The parsed key ring. Invalid entries are skipped and reported once in the log-free problem
    ///     text that accompanies <see cref="SecretEncryptionNotConfiguredException" />; the service keeps
    ///     running (concept §3.5).
    /// </summary>
    private sealed class KeyRing
    {
        private KeyRing(Dictionary<string, byte[]> keys, string? activeKeyId, byte[]? activeKey,
            byte[]? legacyV1Key, IReadOnlyList<string> problems)
        {
            Keys = keys;
            ActiveKeyId = activeKeyId;
            ActiveKey = activeKey;
            LegacyV1Key = legacyV1Key;
            ProblemSuffix = problems.Count == 0 ? string.Empty : " Configuration problems: " + string.Join("; ", problems);
        }

        public Dictionary<string, byte[]> Keys { get; }

        public string? ActiveKeyId { get; }

        public byte[]? ActiveKey { get; }

        public byte[]? LegacyV1Key { get; }

        public string ProblemSuffix { get; }

        public static KeyRing Create(SecretEncryptionOptions options)
        {
            var problems = new List<string>();
            var keys = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var (keyId, encodedKey) in options.Keys ?? new Dictionary<string, string>())
            {
                if (!SecretEnvelope.IsValidKeyId(keyId))
                {
                    problems.Add($"key id '{keyId}' is invalid (1-32 characters of [A-Za-z0-9_-])");
                    continue;
                }

                var key = DecodeKey(encodedKey);
                if (key == null)
                {
                    problems.Add($"key '{keyId}' is not a base64-encoded 32-byte key");
                    continue;
                }

                keys[keyId] = key;
            }

            string? activeKeyId = null;
            byte[]? activeKey = null;
            if (!string.IsNullOrWhiteSpace(options.ActiveKeyId))
            {
                if (keys.TryGetValue(options.ActiveKeyId, out activeKey))
                {
                    // The header carries the key id exactly as configured (AB#5536).
                    activeKeyId = options.ActiveKeyId;
                }
                else
                {
                    problems.Add($"active key id '{options.ActiveKeyId}' is not in the key ring");
                }
            }

            byte[]? legacyKey = null;
            if (!string.IsNullOrWhiteSpace(options.LegacyV1Key))
            {
                legacyKey = DecodeKey(options.LegacyV1Key);
                if (legacyKey == null)
                {
                    problems.Add("LegacyV1Key is not a base64-encoded 32-byte key");
                }
            }

            return new KeyRing(keys, activeKeyId, activeKey, legacyKey, problems);
        }

        private static byte[]? DecodeKey(string? encodedKey)
        {
            if (string.IsNullOrWhiteSpace(encodedKey))
            {
                return null;
            }

            try
            {
                var key = Convert.FromBase64String(encodedKey.Trim());
                return key.Length == LegacyInstanceSecretCrypto.KeyLength ? key : null;
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
