using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.Secrets;

/// <summary>
///     What a reader may learn about a stored Secret value (decisions 2026-10-06, item 2). Exposed by the
///     APIs as <c>isSet</c> (<see cref="Set" />) and <c>keyMissing</c> (<see cref="KeyMissing" />); never
///     the value itself.
/// </summary>
public enum SecretValueState
{
    /// <summary>
    ///     No usable value: <c>null</c>, an empty value, a legacy placeholder still waiting for the
    ///     migration (see <see cref="SecretAttributeConventions.IsLegacyPlaceholder" />), or a corrupt
    ///     stored value (an <c>enc:v2</c> envelope stored as a legacy string). Readers show "not set".
    /// </summary>
    NotSet = 0,

    /// <summary>
    ///     A value is stored and can be decrypted with the key ring (protected with a known key id,
    ///     legacy clear text or <c>enc:v1</c>, or a pending input value).
    /// </summary>
    Set = 1,

    /// <summary>
    ///     A protected value is stored, but its key id is not in the key ring (restore from another
    ///     environment, a removed key). The ciphertext is KEPT: readers show "not set" (<c>isSet = false</c>)
    ///     plus <c>keyMissing = true</c> ("key missing - re-enter"); the value becomes readable again
    ///     automatically once its key is added to the ring. For "required" checks it counts as present.
    /// </summary>
    KeyMissing = 2
}

/// <summary>
///     Storage form of a Secret value as administrators see it (secrets overview, handover §7). Never
///     the value.
/// </summary>
public enum SecretStorageForm
{
    /// <summary>
    ///     <c>null</c> / missing, an empty value, or a legacy placeholder still waiting for the migration.
    /// </summary>
    NotSet = 0,

    /// <summary>
    ///     Legacy clear text still stored (before the encrypt sweep). A pending (unsaved input) value is
    ///     reported as this form as well.
    /// </summary>
    Plaintext = 1,

    /// <summary>
    ///     Legacy <c>enc:v1</c> string (instance key).
    /// </summary>
    EncV1 = 2,

    /// <summary>
    ///     Protected, key id in the key ring.
    /// </summary>
    EncV2 = 3,

    /// <summary>
    ///     Protected, key id NOT in the key ring (kept; reads as not set + key missing).
    /// </summary>
    KeyMissing = 4,

    /// <summary>
    ///     A stored value that can never be read: an <c>enc:v2</c> envelope stored as a legacy string
    ///     (<see cref="SecretValueStates.IsCorrupt" />). Reads as not set, warning logged.
    /// </summary>
    Corrupt = 5
}

/// <summary>
///     Everything a reader may learn about a Secret value without decrypting it: the read state, the
///     storage form, the key id (<see cref="SecretStorageForm.EncV2" /> / <see cref="SecretStorageForm.KeyMissing" />
///     only) and when it was set (<see cref="RtSecretValue.SetAt" />, protected values only).
/// </summary>
/// <param name="State">Read state (<c>isSet = State == Set</c>, <c>keyMissing = State == KeyMissing</c>)</param>
/// <param name="Form">Storage form</param>
/// <param name="KeyId">Key id of a protected value; <c>null</c> otherwise</param>
/// <param name="SetAt">"Set at" (UTC) of a protected value; <c>null</c> for legacy / not set / unknown</param>
public readonly record struct SecretReadInfo(
    SecretValueState State,
    SecretStorageForm Form,
    string? KeyId,
    DateTime? SetAt)
{
    /// <summary>
    ///     True when the value reads as set.
    /// </summary>
    public bool IsSet => State == SecretValueState.Set;

    /// <summary>
    ///     True when a value is stored but its key id is not in the key ring.
    /// </summary>
    public bool KeyMissing => State == SecretValueState.KeyMissing;
}

/// <summary>
///     Pure classification of Secret values into <see cref="SecretValueState" /> (decisions 2026-10-06,
///     item 2). <see cref="ISecretAttributeProtector.GetReadState" /> uses it with the key ring of the
///     process; callers without a protector pass the set of known key ids (or no key ring at all, see
///     <see cref="GetReadState(RtSecretValue?, Func{string?, bool}?)" />).
/// </summary>
public static class SecretValueStates
{
    /// <summary>
    ///     Classifies a stored or pending Secret value. Never decrypts.
    /// </summary>
    /// <param name="value">The value; <c>null</c> = not set</param>
    /// <param name="isKnownKeyId">
    ///     True when a key id is in the key ring. <c>null</c> = the caller has no key ring (e.g. the octo-sdk
    ///     DTO mapper without a protector): every protected value then counts as <see cref="SecretValueState.Set" />
    ///     and <see cref="SecretValueState.KeyMissing" /> is never returned - such a caller must report
    ///     "key missing" as unknown (<c>null</c>), not as <c>false</c>.
    /// </param>
    /// <returns>The state</returns>
    /// <remarks>
    ///     <list type="table">
    ///         <listheader><term>Value</term><description>State</description></listheader>
    ///         <item><term><c>null</c></term><description><see cref="SecretValueState.NotSet" /></description></item>
    ///         <item><term>protected, key id in the ring</term><description><see cref="SecretValueState.Set" /></description></item>
    ///         <item><term>protected, key id NOT in the ring</term><description><see cref="SecretValueState.KeyMissing" /></description></item>
    ///         <item><term>pending (input)</term><description><see cref="SecretValueState.Set" /> when non-empty - a placeholder-looking input is an ordinary value</description></item>
    ///         <item><term>legacy string: empty or legacy placeholder</term><description><see cref="SecretValueState.NotSet" /> (normalised once by the migration)</description></item>
    ///         <item><term>legacy string: <c>enc:v2</c> envelope</term><description><see cref="SecretValueState.NotSet" /> - corrupt (<see cref="IsCorrupt" />), never decrypted</description></item>
    ///         <item><term>legacy string: other (clear text, <c>enc:v1</c>)</term><description><see cref="SecretValueState.Set" /></description></item>
    ///     </list>
    /// </remarks>
    public static SecretValueState GetReadState(RtSecretValue? value, Func<string?, bool>? isKnownKeyId)
    {
        if (value == null)
        {
            return SecretValueState.NotSet;
        }

        switch (value.State)
        {
            case RtSecretValueState.Protected:
                return isKnownKeyId == null || isKnownKeyId(value.KeyId)
                    ? SecretValueState.Set
                    : SecretValueState.KeyMissing;
            case RtSecretValueState.Pending:
                return value.RawValue.Length > 0 ? SecretValueState.Set : SecretValueState.NotSet;
            default:
                return value.RawValue.Length == 0 ||
                       SecretAttributeConventions.IsLegacyPlaceholder(value.RawValue) ||
                       IsCorrupt(value)
                    ? SecretValueState.NotSet
                    : SecretValueState.Set;
        }
    }

    /// <summary>
    ///     Describes a value - read state, storage form, key id and "set at" - without decrypting it. Same
    ///     rules and the same <paramref name="isKnownKeyId" /> contract as
    ///     <see cref="GetReadState(RtSecretValue?, Func{string?, bool}?)" />; without a key ring a protected value
    ///     is <see cref="SecretStorageForm.EncV2" />.
    /// </summary>
    /// <param name="value">The value; <c>null</c> = not set</param>
    /// <param name="isKnownKeyId">True when a key id is in the key ring; <c>null</c> = no key ring</param>
    /// <returns>The description</returns>
    public static SecretReadInfo Describe(RtSecretValue? value, Func<string?, bool>? isKnownKeyId)
    {
        var state = GetReadState(value, isKnownKeyId);
        if (value == null)
        {
            return new SecretReadInfo(state, SecretStorageForm.NotSet, null, null);
        }

        switch (value.State)
        {
            case RtSecretValueState.Protected:
                return new SecretReadInfo(state,
                    state == SecretValueState.KeyMissing ? SecretStorageForm.KeyMissing : SecretStorageForm.EncV2,
                    value.KeyId, value.SetAt);
            case RtSecretValueState.Pending:
                return new SecretReadInfo(state,
                    value.RawValue.Length == 0 ? SecretStorageForm.NotSet : SecretStorageForm.Plaintext, null, null);
            default:
                if (IsCorrupt(value))
                {
                    return new SecretReadInfo(state, SecretStorageForm.Corrupt, null, null);
                }

                if (state == SecretValueState.NotSet)
                {
                    return new SecretReadInfo(state, SecretStorageForm.NotSet, null, null);
                }

                return new SecretReadInfo(state,
                    SecretEnvelope.TryParse(value.RawValue, out var info) && info.Version == 1
                        ? SecretStorageForm.EncV1
                        : SecretStorageForm.Plaintext,
                    null, null);
        }
    }

    /// <summary>
    ///     Classifies with an explicit set of known key ids (compared case-insensitively like the key ring).
    /// </summary>
    /// <param name="value">The value</param>
    /// <param name="knownKeyIds">Key ids of the key ring</param>
    /// <returns>The state</returns>
    public static SecretValueState GetReadState(RtSecretValue? value, IEnumerable<string> knownKeyIds)
    {
        ArgumentNullException.ThrowIfNull(knownKeyIds);
        var known = new HashSet<string>(knownKeyIds, StringComparer.OrdinalIgnoreCase);
        return GetReadState(value, kid => kid != null && known.Contains(kid));
    }

    /// <summary>
    ///     True for a value that is stored but can never be read: an <c>enc:v2</c> envelope found as a legacy
    ///     string (AB#5532 - nothing legitimate writes one; it was copied there). Readers treat it as not set.
    /// </summary>
    /// <param name="value">The value</param>
    /// <returns>True when corrupt</returns>
    public static bool IsCorrupt(RtSecretValue? value)
    {
        return value is { IsLegacyPlaintext: true } &&
               SecretEnvelope.TryParse(value.RawValue, out var info) && info.Version != 1;
    }
}
