namespace Meshmakers.Octo.Runtime.Contracts.Secrets;

/// <summary>
///     Who encrypts or decrypts a secret file - tags of the <c>octo.secrets.file_protect</c> and
///     <c>octo.secrets.file_unprotect</c> counters. Every member is optional; the service falls back to
///     the process' service name.
/// </summary>
/// <param name="TenantId">Tenant the file belongs to</param>
/// <param name="Purpose">What the file is, e.g. <c>presweep</c> or <c>tenant-dump</c></param>
/// <param name="Service">Calling service; defaults to <c>OTEL_SERVICE_NAME</c> or the entry assembly</param>
public sealed record SecretFileContext(
    string? TenantId = null,
    string? Purpose = null,
    string? Service = null);

/// <summary>
///     The clear-text header of an encrypted secret file (format <c>OCTOENC1</c>). Reading it needs no key.
/// </summary>
/// <param name="Version">Format version (currently 1)</param>
/// <param name="KeyId">Key id of the key ring entry the file key is wrapped with</param>
/// <param name="CreatedAt">UTC time the file was encrypted (millisecond precision)</param>
/// <param name="ChunkSize">Plaintext bytes per chunk</param>
/// <param name="HeaderLength">Length of the header in bytes; the first chunk starts at this offset</param>
public sealed record SecretFileHeader(
    int Version,
    string KeyId,
    DateTime CreatedAt,
    int ChunkSize,
    int HeaderLength);

/// <summary>
///     Constants of the encrypted secret file format <c>OCTOENC1</c> (AB#5559).
/// </summary>
public static class SecretFileFormat
{
    /// <summary>
    ///     File extension of encrypted secret files, e.g. <c>tenant-20261006.presweep.octoenc</c>.
    /// </summary>
    public const string FileExtension = ".octoenc";

    /// <summary>
    ///     ASCII magic at offset 0 of every encrypted secret file.
    /// </summary>
    public const string Magic = "OCTOENC1";

    /// <summary>
    ///     Current format version (the byte after the magic).
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    ///     Default plaintext chunk size (1 MiB).
    /// </summary>
    public const int DefaultChunkSize = 1024 * 1024;

    /// <summary>
    ///     Smallest chunk size a file may declare.
    /// </summary>
    public const int MinChunkSize = 1024;

    /// <summary>
    ///     Largest chunk size a file may declare (bounds the memory a reader allocates).
    /// </summary>
    public const int MaxChunkSize = 16 * 1024 * 1024;
}

/// <summary>
///     Encrypts and decrypts files (e.g. tenant dumps taken before a secret sweep) with the instance key
///     ring (AB#5559). Implemented in Runtime.Engine and registered by <c>AddRuntimeEngine()</c>; the key
///     material never leaves the engine.
/// </summary>
/// <remarks>
///     <para>
///         Format <c>OCTOENC1</c> (see <c>docs/secret-sweep-dump-storage.md</c>, "Encrypted dump file
///         format"): a random 256-bit file key per file, wrapped with AES-256-GCM under a key derived with
///         HKDF-SHA256 from the ring key of the active key id; the body is a sequence of AES-256-GCM chunks
///         bound to the header, their index and a final flag, so tampering, truncation and reordering are
///         detected. Both directions stream with memory bounded by the chunk size.
///     </para>
///     <para>
///         <see cref="UnprotectAsync" /> writes the plaintext of every verified chunk before it reads the
///         next one. When it throws, the output holds a prefix of the plaintext and must be discarded.
///     </para>
///     <para>
///         A key id must stay in the key ring until the newest file encrypted with it has expired; without
///         the key the file is unreadable.
///     </para>
/// </remarks>
public interface ISecretFileProtector
{
    /// <summary>
    ///     True when an active key is configured, i.e. <see cref="ProtectAsync" /> can work.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    ///     Encrypts <paramref name="plaintext" /> (read to its end) into <paramref name="output" /> with the
    ///     active key id. Neither stream needs to be seekable; neither is disposed.
    /// </summary>
    /// <param name="plaintext">Clear-text input</param>
    /// <param name="output">Receives the encrypted file</param>
    /// <param name="context">Caller, for the metrics</param>
    /// <param name="cancellationToken">Cancellation</param>
    /// <exception cref="SecretEncryptionNotConfiguredException">No active key is configured</exception>
    Task ProtectAsync(Stream plaintext, Stream output, SecretFileContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Decrypts an encrypted file from <paramref name="input" /> (read to its end) into
    ///     <paramref name="output" />. Neither stream needs to be seekable; neither is disposed.
    /// </summary>
    /// <param name="input">Encrypted file, positioned at its first byte</param>
    /// <param name="output">Receives the plaintext; discard it when the call throws</param>
    /// <param name="context">Caller, for the metrics</param>
    /// <param name="cancellationToken">Cancellation</param>
    /// <exception cref="InvalidSecretFileException">Not an encrypted secret file, or tampered, truncated or reordered</exception>
    /// <exception cref="UnknownSecretKeyIdException">The file's key id is not in the key ring</exception>
    /// <exception cref="SecretEncryptionNotConfiguredException">No keys are configured at all</exception>
    Task UnprotectAsync(Stream input, Stream output, SecretFileContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reads the clear-text header from <paramref name="input" />, which is left positioned after the
    ///     header. Needs no key and does not verify the file's integrity.
    /// </summary>
    /// <param name="input">Encrypted file, positioned at its first byte</param>
    /// <returns>The header</returns>
    /// <exception cref="InvalidSecretFileException">Not an encrypted secret file (or an unsupported version)</exception>
    SecretFileHeader ReadHeader(Stream input);

    /// <summary>
    ///     True when the key id of <paramref name="header" /> is in the key ring, i.e. the file can be
    ///     decrypted (unless it was tampered with).
    /// </summary>
    /// <param name="header">Header from <see cref="ReadHeader" /></param>
    bool CanUnprotect(SecretFileHeader header);
}
