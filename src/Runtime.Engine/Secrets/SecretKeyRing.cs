using Meshmakers.Octo.Runtime.Contracts.Secrets;

namespace Meshmakers.Octo.Runtime.Engine.Secrets;

/// <summary>
///     The parsed key ring of <see cref="SecretEncryptionOptions" />, shared by
///     <see cref="SecretAttributeProtector" /> and <see cref="SecretFileProtector" />. Invalid entries are
///     skipped and reported once in the log-free problem text that accompanies
///     <see cref="SecretEncryptionNotConfiguredException" />; the service keeps running (concept §3.5).
/// </summary>
internal sealed class SecretKeyRing
{
    private SecretKeyRing(Dictionary<string, byte[]> keys, string? activeKeyId, byte[]? activeKey,
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

    public static SecretKeyRing Create(SecretEncryptionOptions options)
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

        return new SecretKeyRing(keys, activeKeyId, activeKey, legacyKey, problems);
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
