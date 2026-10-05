using System.Security.Cryptography;

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
