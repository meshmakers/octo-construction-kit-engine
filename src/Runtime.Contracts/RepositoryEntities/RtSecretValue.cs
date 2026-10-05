using Meshmakers.Octo.Runtime.Contracts.Secrets;

namespace Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

/// <summary>
///     State of an <see cref="RtSecretValue" />.
/// </summary>
public enum RtSecretValueState
{
    /// <summary>
    ///     A plaintext received on a write path that has not been encrypted yet. Only exists between
    ///     the API input and the write step of the repository (AB#5532); the BSON serializer refuses
    ///     to store it (AB#5533).
    /// </summary>
    Pending = 0,

    /// <summary>
    ///     An encrypted value (envelope <c>enc:v2:&lt;kid&gt;:...</c>) as read from the database.
    /// </summary>
    Protected = 1,

    /// <summary>
    ///     A string found in a Secret slot while data is migrated: either a clear-text value or an
    ///     <c>enc:v1:</c> value of the older instance-secret format.
    /// </summary>
    LegacyPlaintext = 2
}

/// <summary>
///     In-memory value of a <c>Secret</c> attribute (AB#5528, concept §3.3). It never hands out
///     its plaintext: <see cref="ToString" /> returns <c>***</c>, and the plaintext of a
///     <see cref="RtSecretValueState.Pending" /> or <see cref="RtSecretValueState.LegacyPlaintext" />
///     value is only reachable through <see cref="ISecretAttributeProtector" /> (internal access).
/// </summary>
/// <remarks>
///     Equality: two protected values are equal when their envelopes are equal (the same
///     ciphertext, i.e. the value was carried over and not re-entered). Pending and legacy values
///     compare by state and value. The hash code never derives from plaintext.
/// </remarks>
public sealed class RtSecretValue : IEquatable<RtSecretValue>
{
    /// <summary>
    ///     Text returned by <see cref="ToString" /> - never the value.
    /// </summary>
    public const string Mask = "***";

    private readonly string _value;

    private RtSecretValue(RtSecretValueState state, string value)
    {
        State = state;
        _value = value;
    }

    /// <summary>
    ///     State of the value.
    /// </summary>
    public RtSecretValueState State { get; }

    /// <summary>
    ///     True for <see cref="RtSecretValueState.Pending" />.
    /// </summary>
    public bool IsPending => State == RtSecretValueState.Pending;

    /// <summary>
    ///     True for <see cref="RtSecretValueState.Protected" />.
    /// </summary>
    public bool IsProtected => State == RtSecretValueState.Protected;

    /// <summary>
    ///     True for <see cref="RtSecretValueState.LegacyPlaintext" />.
    /// </summary>
    public bool IsLegacyPlaintext => State == RtSecretValueState.LegacyPlaintext;

    /// <summary>
    ///     The envelope of a protected value (ciphertext, safe to store and copy between
    ///     repositories of the same key ring); <c>null</c> for every other state.
    /// </summary>
    public string? Envelope => State == RtSecretValueState.Protected ? _value : null;

    /// <summary>
    ///     The key id of a protected value; <c>null</c> for every other state.
    /// </summary>
    public string? KeyId =>
        State == RtSecretValueState.Protected && SecretEnvelope.TryParse(_value, out var info) ? info.KeyId : null;

    /// <summary>
    ///     Creates a pending value from a plaintext received on a write path.
    /// </summary>
    /// <param name="plaintext">The plaintext</param>
    /// <returns>A pending value</returns>
    public static RtSecretValue Pending(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return new RtSecretValue(RtSecretValueState.Pending, plaintext);
    }

    /// <summary>
    ///     Creates a protected value from an <c>enc:v2</c> envelope (e.g. read from the database).
    /// </summary>
    /// <param name="envelope">The envelope</param>
    /// <returns>A protected value</returns>
    /// <exception cref="ArgumentException">The string is not a structurally valid <c>enc:v2</c> envelope</exception>
    public static RtSecretValue Protected(string envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (!SecretEnvelope.TryParse(envelope, out var info) || info.Version != SecretEnvelope.CurrentVersion)
        {
            // Never echo the value: it might be a plaintext handed in by mistake.
            throw new ArgumentException("The value is not a valid 'enc:v2' secret envelope.", nameof(envelope));
        }

        return new RtSecretValue(RtSecretValueState.Protected, envelope);
    }

    /// <summary>
    ///     Creates a legacy value from a string found in a Secret slot (clear text or <c>enc:v1</c>).
    /// </summary>
    /// <param name="value">The stored string</param>
    /// <returns>A legacy value</returns>
    public static RtSecretValue LegacyPlaintext(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new RtSecretValue(RtSecretValueState.LegacyPlaintext, value);
    }

    /// <summary>
    ///     The raw value of a <see cref="RtSecretValueState.Pending" /> or
    ///     <see cref="RtSecretValueState.LegacyPlaintext" /> value (plaintext or <c>enc:v1</c>), or the
    ///     envelope of a protected one. Internal on purpose: the protector (Runtime.Engine) and the
    ///     BSON serializer (Runtime.Engine.MongoDb, AB#5533 - it writes legacy values back unchanged)
    ///     are the only readers.
    /// </summary>
    internal string RawValue => _value;

    /// <inheritdoc />
    public override string ToString()
    {
        return Mask;
    }

    /// <inheritdoc />
    public bool Equals(RtSecretValue? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return State == other.State && string.Equals(_value, other._value, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is RtSecretValue other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        // Only the envelope (ciphertext) contributes; a plaintext never feeds a hash.
        return State == RtSecretValueState.Protected
            ? HashCode.Combine(State, StringComparer.Ordinal.GetHashCode(_value))
            : State.GetHashCode();
    }

    /// <summary>
    ///     Equality operator.
    /// </summary>
    public static bool operator ==(RtSecretValue? left, RtSecretValue? right)
    {
        return left is null ? right is null : left.Equals(right);
    }

    /// <summary>
    ///     Inequality operator.
    /// </summary>
    public static bool operator !=(RtSecretValue? left, RtSecretValue? right)
    {
        return !(left == right);
    }
}
