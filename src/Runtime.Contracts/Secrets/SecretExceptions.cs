using System.Security.Cryptography;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.Secrets;

/// <summary>
///     Thrown when a secret must be encrypted or decrypted but the key ring
///     (configuration section <c>SecretEncryption</c>) does not provide the needed key material.
/// </summary>
public class SecretEncryptionNotConfiguredException : InvalidOperationException
{
    /// <summary>
    ///     Creates a new instance.
    /// </summary>
    public SecretEncryptionNotConfiguredException(string message) : base(message)
    {
    }
}

/// <summary>
///     Thrown in strict mode (configuration <c>SecretEncryption:StrictMode</c>, concept decision 10,
///     §5.2 phase 5) when a <c>Secret</c> attribute is read that is still stored as legacy clear text.
///     The message never contains the value; run the encrypt sweep (or re-enter the secret).
/// </summary>
public class LegacyPlaintextSecretRejectedException : InvalidOperationException
{
    /// <summary>
    ///     Creates a new instance.
    /// </summary>
    /// <param name="tenantId">Tenant of the entity, when known</param>
    /// <param name="ckTypeId">CK type of the entity, when known</param>
    /// <param name="attributeName">Name of the Secret attribute, when known</param>
    public LegacyPlaintextSecretRejectedException(string? tenantId = null, string? ckTypeId = null,
        string? attributeName = null)
        : base($"Secret attribute '{attributeName ?? "?"}' of '{ckTypeId ?? "?"}' (tenant '{tenantId ?? "?"}') is " +
               "still stored as clear text and strict mode (SecretEncryption:StrictMode) rejects legacy plaintext " +
               "reads. Run the secret encrypt sweep or enter the secret again.")
    {
        TenantId = tenantId;
        CkTypeId = ckTypeId;
        AttributeName = attributeName;
    }

    /// <summary>
    ///     Tenant of the entity, when known.
    /// </summary>
    public string? TenantId { get; }

    /// <summary>
    ///     CK type of the entity, when known.
    /// </summary>
    public string? CkTypeId { get; }

    /// <summary>
    ///     Name of the Secret attribute, when known.
    /// </summary>
    public string? AttributeName { get; }
}

/// <summary>
///     Thrown when an <c>enc:v2</c> envelope names a key id that is not in the key ring - typically a
///     value restored from another environment (decision 5: such secrets become "not set").
/// </summary>
public class UnknownSecretKeyIdException : CryptographicException
{
    /// <summary>
    ///     Creates a new instance.
    /// </summary>
    public UnknownSecretKeyIdException(string keyId)
        : base($"The secret was encrypted with key id '{keyId}', which is not in the key ring " +
               "(SecretEncryption:Keys). It was written by another environment or the key was removed; " +
               "the secret has to be entered again.")
    {
        KeyId = keyId;
    }

    /// <summary>
    ///     The unknown key id.
    /// </summary>
    public string KeyId { get; }
}

/// <summary>
///     Thrown when a query touches a <c>Secret</c> attribute (AB#5528, concept §4.4) with anything
///     other than an <c>IS_NULL</c> / <c>IS_NOT_NULL</c> field filter: comparison operators, sort,
///     attribute (text) search, aggregations and group-by are refused, because they would either
///     compare ciphertext (meaningless) or reveal something about the plaintext.
/// </summary>
/// <remarks>
///     Defined in the engine contracts so every repository implementation throws the same type and the
///     API layer can map it to the error code <c>SecretAttributeNotQueryable</c>. The message never
///     contains a value.
/// </remarks>
[Serializable]
public sealed class SecretAttributeNotQueryableException : PersistenceException
{
    /// <summary>
    ///     Creates a new instance.
    /// </summary>
    /// <param name="attributePath">The attribute path of the Secret attribute</param>
    /// <param name="operation">What was attempted, e.g. <c>filter operator 'Equals'</c> or <c>sort</c></param>
    /// <param name="entityName">The CK type or record the path was resolved against</param>
    public SecretAttributeNotQueryableException(string attributePath, string operation, string entityName)
        : base($"Attribute '{attributePath}' of '{entityName}' is a Secret attribute and cannot be used for " +
               $"{operation}. Secret attributes only support the field filter operators IS_NULL and IS_NOT_NULL.")
    {
        AttributePath = attributePath;
        Operation = operation;
        EntityName = entityName;
    }

    /// <summary>
    ///     The attribute path of the Secret attribute.
    /// </summary>
    public string AttributePath { get; }

    /// <summary>
    ///     What was attempted with the attribute.
    /// </summary>
    public string Operation { get; }

    /// <summary>
    ///     The CK type or record the path was resolved against.
    /// </summary>
    public string EntityName { get; }
}

/// <summary>
///     Thrown by a repository when an <see cref="RtSecretValue" /> that cannot be persisted is about to
///     be written (AB#5528, concept §3.3 / §8 "write paths that skip encryption"). A
///     <see cref="RtSecretValueState.Pending" /> value is a plaintext the engine write step has not
///     encrypted; refusing it keeps plaintext out of the database even when a write path forgets the
///     protector.
/// </summary>
/// <remarks>
///     The message never contains the value.
/// </remarks>
public sealed class SecretValueNotStorableException : InvalidOperationException
{
    /// <summary>
    ///     Creates a new instance.
    /// </summary>
    /// <param name="state">State of the refused value</param>
    public SecretValueNotStorableException(RtSecretValueState state)
        : base(state == RtSecretValueState.Pending
            ? "A pending (unencrypted) secret value cannot be stored. The engine write step must protect it " +
              "with ISecretAttributeProtector before it reaches the repository (AB#5532)."
            : $"A secret value in state '{state}' cannot be stored as a secret. Protect it with " +
              "ISecretAttributeProtector first; only protected envelopes are written as secrets.")
    {
        State = state;
    }

    /// <summary>
    ///     State of the refused value.
    /// </summary>
    public RtSecretValueState State { get; }
}
