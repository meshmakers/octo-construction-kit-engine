using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Meshmakers.Octo.Runtime.Contracts.Secrets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Runtime.Engine.Secrets;

/// <summary>
///     Streaming implementation of <see cref="ISecretFileProtector" /> (format <c>OCTOENC1</c>, AB#5559)
///     over the key ring in <see cref="SecretEncryptionOptions" />. Registered as a singleton by
///     <c>AddRuntimeEngine()</c>. Never logs file content or key material.
/// </summary>
/// <remarks>
///     Layout (integers big-endian):
///     <code>
///     header:  "OCTOENC1" (8) | version (1) | kid length n (1) | kid (n, ASCII) | createdAt unix ms (8)
///              | chunk size (4) | base nonce (12) | wrap nonce (12) | wrapped file key (32) | wrap tag (16)
///     body:    chunk*  where chunk = AES-256-GCM ciphertext (chunk size, last chunk shorter) | tag (16)
///     </code>
///     The file key is wrapped under <c>HKDF-SHA256(ring[kid], info "octo-secret-file-v1")</c> with the
///     header bytes up to the base nonce as associated data. Chunk i uses nonce = base nonce XOR i (in the
///     last 8 bytes) and associated data = SHA-256(header) | i (8) | final flag (1). The last chunk always
///     holds fewer than chunk size bytes (possibly none), so a missing tail is detected.
/// </remarks>
internal sealed class SecretFileProtector : ISecretFileProtector
{
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int FileKeyLength = 32;
    private const int PrefixLength = 10; // magic + version + kid length
    private const int FixedTailLength = 8 + 4 + NonceLength + NonceLength + FileKeyLength + TagLength;
    private const int ChunkAadLength = 32 + 8 + 1;

    private const string ResultOk = "ok";
    private const string ResultUnknownKeyId = "unknown_key_id";
    private const string ResultInvalid = "invalid";
    private const string ResultNotConfigured = "not_configured";

    private static readonly byte[] MagicBytes = Encoding.ASCII.GetBytes(SecretFileFormat.Magic);
    private static readonly byte[] KdfInfo = Encoding.ASCII.GetBytes("octo-secret-file-v1");

    private static readonly string? DefaultServiceName =
        Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") is { Length: > 0 } serviceName
            ? serviceName
            : Assembly.GetEntryAssembly()?.GetName().Name;

    private readonly int _chunkSize;
    private readonly Lazy<SecretKeyRing> _keyRing;
    private readonly ILogger<SecretFileProtector> _logger;

    public SecretFileProtector(IOptions<SecretEncryptionOptions> options, ILogger<SecretFileProtector> logger)
        : this(options, logger, SecretFileFormat.DefaultChunkSize)
    {
    }

    internal SecretFileProtector(IOptions<SecretEncryptionOptions> options, ILogger<SecretFileProtector> logger,
        int chunkSize)
    {
        if (chunkSize < SecretFileFormat.MinChunkSize || chunkSize > SecretFileFormat.MaxChunkSize)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), chunkSize,
                $"The chunk size must be between {SecretFileFormat.MinChunkSize} and {SecretFileFormat.MaxChunkSize} bytes.");
        }

        _logger = logger;
        _chunkSize = chunkSize;
        var value = options.Value;
        _keyRing = new Lazy<SecretKeyRing>(() => SecretKeyRing.Create(value),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public bool IsConfigured => _keyRing.Value.ActiveKeyId != null;

    /// <inheritdoc />
    public async Task ProtectAsync(Stream plaintext, Stream output, SecretFileContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(output);

        var keyRing = _keyRing.Value;
        if (keyRing.ActiveKeyId == null || keyRing.ActiveKey == null)
        {
            throw new SecretEncryptionNotConfiguredException(
                "Cannot encrypt a secret file: no active key is configured (SecretEncryption:ActiveKeyId / " +
                "SecretEncryption:Keys)." + keyRing.ProblemSuffix);
        }

        var keyId = keyRing.ActiveKeyId;
        var kidBytes = Encoding.ASCII.GetBytes(keyId);
        var header = new byte[PrefixLength + kidBytes.Length + FixedTailLength];
        MagicBytes.CopyTo(header, 0);
        header[8] = SecretFileFormat.CurrentVersion;
        header[9] = (byte)kidBytes.Length;
        kidBytes.CopyTo(header, PrefixLength);
        var offset = PrefixLength + kidBytes.Length;
        BinaryPrimitives.WriteInt64BigEndian(header.AsSpan(offset, 8), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(offset + 8, 4), _chunkSize);
        var baseNonceOffset = offset + 12;
        var wrapNonceOffset = baseNonceOffset + NonceLength;
        var wrappedKeyOffset = wrapNonceOffset + NonceLength;
        var wrapTagOffset = wrappedKeyOffset + FileKeyLength;
        RandomNumberGenerator.Fill(header.AsSpan(baseNonceOffset, NonceLength));
        RandomNumberGenerator.Fill(header.AsSpan(wrapNonceOffset, NonceLength));

        var fileKey = new byte[FileKeyLength];
        var chunkBuffer = new byte[_chunkSize];
        try
        {
            RandomNumberGenerator.Fill(fileKey);
            var kek = DeriveKeyEncryptionKey(keyRing.ActiveKey);
            try
            {
                using var wrap = new AesGcm(kek, TagLength);
                wrap.Encrypt(header.AsSpan(wrapNonceOffset, NonceLength), fileKey,
                    header.AsSpan(wrappedKeyOffset, FileKeyLength), header.AsSpan(wrapTagOffset, TagLength),
                    header.AsSpan(0, wrapNonceOffset));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(kek);
            }

            await output.WriteAsync(header, cancellationToken).ConfigureAwait(false);

            var headerHash = SHA256.HashData(header);
            var baseNonce = header.AsSpan(baseNonceOffset, NonceLength).ToArray();
            var sealedBuffer = new byte[_chunkSize + TagLength];
            using var aes = new AesGcm(fileKey, TagLength);
            ulong index = 0;
            long totalBytes = 0;
            while (true)
            {
                var read = await plaintext.ReadAtLeastAsync(chunkBuffer, _chunkSize, throwOnEndOfStream: false,
                    cancellationToken).ConfigureAwait(false);
                var isFinal = read < _chunkSize;
                SealChunk(aes, baseNonce, headerHash, index, isFinal, chunkBuffer.AsSpan(0, read),
                    sealedBuffer.AsSpan(0, read + TagLength));
                await output.WriteAsync(sealedBuffer.AsMemory(0, read + TagLength), cancellationToken)
                    .ConfigureAwait(false);
                totalBytes += read;
                if (isFinal)
                {
                    break;
                }

                index++;
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            SecretDiagnostics.FileProtects.Add(1, BuildTags(keyId, ResultOk, context));
            _logger.LogInformation(
                "Encrypted a secret file with key id {KeyId} ({Bytes} bytes, {Chunks} chunks; tenant {TenantId}, purpose {Purpose})",
                keyId, totalBytes, index + 1, context?.TenantId, context?.Purpose);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fileKey);
            CryptographicOperations.ZeroMemory(chunkBuffer);
        }
    }

    /// <inheritdoc />
    public async Task UnprotectAsync(Stream input, Stream output, SecretFileContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        string? keyId = null;
        try
        {
            var parsed = await ReadParsedHeaderAsync(input, cancellationToken).ConfigureAwait(false);
            keyId = parsed.Header.KeyId;
            var keyRing = _keyRing.Value;
            if (keyRing.Keys.Count == 0)
            {
                throw new SecretEncryptionNotConfiguredException(
                    "Cannot decrypt a secret file: no keys are configured (SecretEncryption:Keys)." +
                    keyRing.ProblemSuffix);
            }

            if (!keyRing.Keys.TryGetValue(keyId, out var ringKey))
            {
                throw new UnknownSecretKeyIdException(keyId);
            }

            var fileKey = UnwrapFileKey(parsed, ringKey);
            var chunkSize = parsed.Header.ChunkSize;
            var sealedBuffer = new byte[chunkSize + TagLength];
            var plainBuffer = new byte[chunkSize];
            try
            {
                using var aes = new AesGcm(fileKey, TagLength);
                ulong index = 0;
                while (true)
                {
                    var read = await input.ReadAtLeastAsync(sealedBuffer, sealedBuffer.Length,
                        throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
                    if (read < TagLength)
                    {
                        throw new InvalidSecretFileException(
                            $"The secret file is truncated (chunk {index} is missing or incomplete).");
                    }

                    var isFinal = read < sealedBuffer.Length;
                    var plainLength = read - TagLength;
                    OpenChunk(aes, parsed.BaseNonce, parsed.HeaderHash, index, isFinal,
                        sealedBuffer.AsSpan(0, read), plainBuffer.AsSpan(0, plainLength));
                    await output.WriteAsync(plainBuffer.AsMemory(0, plainLength), cancellationToken)
                        .ConfigureAwait(false);
                    if (isFinal)
                    {
                        break;
                    }

                    index++;
                }

                var trailing = new byte[1];
                if (await input.ReadAsync(trailing, cancellationToken).ConfigureAwait(false) != 0)
                {
                    throw new InvalidSecretFileException("The secret file has data after its final chunk.");
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                SecretDiagnostics.FileUnprotects.Add(1, BuildTags(keyId, ResultOk, context));
                _logger.LogInformation(
                    "Decrypted a secret file with key id {KeyId} ({Chunks} chunks; tenant {TenantId}, purpose {Purpose})",
                    keyId, index + 1, context?.TenantId, context?.Purpose);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(fileKey);
                CryptographicOperations.ZeroMemory(plainBuffer);
            }
        }
        catch (UnknownSecretKeyIdException)
        {
            ReportFailure(keyId, ResultUnknownKeyId, context);
            throw;
        }
        catch (InvalidSecretFileException)
        {
            ReportFailure(keyId, ResultInvalid, context);
            throw;
        }
        catch (SecretEncryptionNotConfiguredException)
        {
            ReportFailure(keyId, ResultNotConfigured, context);
            throw;
        }
    }

    /// <inheritdoc />
    public SecretFileHeader ReadHeader(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var prefix = new byte[PrefixLength];
        ReadExactly(input, prefix);
        var kidLength = ValidatePrefix(prefix);
        var header = new byte[PrefixLength + kidLength + FixedTailLength];
        prefix.CopyTo(header, 0);
        ReadExactly(input, header.AsSpan(PrefixLength));
        return ParseHeader(header).Header;
    }

    /// <inheritdoc />
    public bool CanUnprotect(SecretFileHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return header.Version == SecretFileFormat.CurrentVersion && _keyRing.Value.Keys.ContainsKey(header.KeyId);
    }

    private void ReportFailure(string? keyId, string result, SecretFileContext? context)
    {
        SecretDiagnostics.FileUnprotects.Add(1, BuildTags(keyId, result, context));
        _logger.LogWarning(
            "Decrypting a secret file failed ({Result}, key id {KeyId}; tenant {TenantId}, purpose {Purpose})",
            result, keyId, context?.TenantId, context?.Purpose);
    }

    private static byte[] DeriveKeyEncryptionKey(byte[] ringKey)
    {
        // A ring key is never used directly for a second purpose: derive a dedicated wrapping key.
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, ringKey, FileKeyLength, salt: [], info: KdfInfo);
    }

    private static byte[] UnwrapFileKey(ParsedHeader parsed, byte[] ringKey)
    {
        var kek = DeriveKeyEncryptionKey(ringKey);
        var fileKey = new byte[FileKeyLength];
        try
        {
            using var wrap = new AesGcm(kek, TagLength);
            wrap.Decrypt(parsed.WrapNonce, parsed.WrappedKey, parsed.WrapTag, fileKey, parsed.WrapAad);
            return fileKey;
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(fileKey);
            throw new InvalidSecretFileException(
                "The secret file header was tampered with or does not match its key id.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    private static void SealChunk(AesGcm aes, byte[] baseNonce, byte[] headerHash, ulong index, bool isFinal,
        ReadOnlySpan<byte> plaintext, Span<byte> destination)
    {
        Span<byte> nonce = stackalloc byte[NonceLength];
        Span<byte> aad = stackalloc byte[ChunkAadLength];
        BuildChunkNonceAndAad(baseNonce, headerHash, index, isFinal, nonce, aad);
        aes.Encrypt(nonce, plaintext, destination[..plaintext.Length], destination[plaintext.Length..], aad);
    }

    private static void OpenChunk(AesGcm aes, byte[] baseNonce, byte[] headerHash, ulong index, bool isFinal,
        ReadOnlySpan<byte> sealedChunk, Span<byte> destination)
    {
        Span<byte> nonce = stackalloc byte[NonceLength];
        Span<byte> aad = stackalloc byte[ChunkAadLength];
        BuildChunkNonceAndAad(baseNonce, headerHash, index, isFinal, nonce, aad);
        var ciphertextLength = sealedChunk.Length - TagLength;
        try
        {
            aes.Decrypt(nonce, sealedChunk[..ciphertextLength], sealedChunk[ciphertextLength..], destination, aad);
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(destination);
            throw new InvalidSecretFileException(
                $"Chunk {index} of the secret file failed authentication (tampered, truncated or reordered).", ex);
        }
    }

    private static void BuildChunkNonceAndAad(byte[] baseNonce, byte[] headerHash, ulong index, bool isFinal,
        Span<byte> nonce, Span<byte> aad)
    {
        baseNonce.CopyTo(nonce);
        var counter = nonce[(NonceLength - 8)..];
        BinaryPrimitives.WriteUInt64BigEndian(counter, BinaryPrimitives.ReadUInt64BigEndian(counter) ^ index);

        headerHash.CopyTo(aad);
        BinaryPrimitives.WriteUInt64BigEndian(aad.Slice(32, 8), index);
        aad[40] = isFinal ? (byte)1 : (byte)0;
    }

    private static async Task<ParsedHeader> ReadParsedHeaderAsync(Stream input, CancellationToken cancellationToken)
    {
        var prefix = new byte[PrefixLength];
        await ReadExactlyAsync(input, prefix, cancellationToken).ConfigureAwait(false);
        var kidLength = ValidatePrefix(prefix);
        var header = new byte[PrefixLength + kidLength + FixedTailLength];
        prefix.CopyTo(header, 0);
        await ReadExactlyAsync(input, header.AsMemory(PrefixLength), cancellationToken).ConfigureAwait(false);
        return ParseHeader(header);
    }

    private static int ValidatePrefix(ReadOnlySpan<byte> prefix)
    {
        if (!prefix[..MagicBytes.Length].SequenceEqual(MagicBytes))
        {
            throw new InvalidSecretFileException("The stream is not an encrypted secret file (magic mismatch).");
        }

        if (prefix[8] != SecretFileFormat.CurrentVersion)
        {
            throw new InvalidSecretFileException(
                $"The secret file has the unsupported format version {prefix[8]}.");
        }

        var kidLength = prefix[9];
        if (kidLength is < 1 or > 32)
        {
            throw new InvalidSecretFileException("The secret file header has an invalid key id length.");
        }

        return kidLength;
    }

    private static ParsedHeader ParseHeader(byte[] header)
    {
        var kidLength = header[9];
        var keyId = Encoding.ASCII.GetString(header, PrefixLength, kidLength);
        if (!SecretEnvelope.IsValidKeyId(keyId))
        {
            throw new InvalidSecretFileException("The secret file header has an invalid key id.");
        }

        var offset = PrefixLength + kidLength;
        var createdAtMs = BinaryPrimitives.ReadInt64BigEndian(header.AsSpan(offset, 8));
        var chunkSize = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(offset + 8, 4));
        if (chunkSize < SecretFileFormat.MinChunkSize || chunkSize > SecretFileFormat.MaxChunkSize)
        {
            throw new InvalidSecretFileException("The secret file header has an invalid chunk size.");
        }

        DateTime createdAt;
        try
        {
            createdAt = DateTimeOffset.FromUnixTimeMilliseconds(createdAtMs).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new InvalidSecretFileException("The secret file header has an invalid creation time.", ex);
        }

        var baseNonceOffset = offset + 12;
        var wrapNonceOffset = baseNonceOffset + NonceLength;
        var wrappedKeyOffset = wrapNonceOffset + NonceLength;
        var wrapTagOffset = wrappedKeyOffset + FileKeyLength;
        return new ParsedHeader(
            new SecretFileHeader(header[8], keyId, createdAt, chunkSize, header.Length),
            header.AsSpan(baseNonceOffset, NonceLength).ToArray(),
            header.AsSpan(wrapNonceOffset, NonceLength).ToArray(),
            header.AsSpan(wrappedKeyOffset, FileKeyLength).ToArray(),
            header.AsSpan(wrapTagOffset, TagLength).ToArray(),
            header.AsSpan(0, wrapNonceOffset).ToArray(),
            SHA256.HashData(header));
    }

    private static void ReadExactly(Stream input, Span<byte> buffer)
    {
        try
        {
            input.ReadExactly(buffer);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidSecretFileException("The secret file is truncated (incomplete header).", ex);
        }
    }

    private static async Task ReadExactlyAsync(Stream input, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        try
        {
            await input.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidSecretFileException("The secret file is truncated (incomplete header).", ex);
        }
    }

    private static TagList BuildTags(string? keyId, string result, SecretFileContext? context)
    {
        var tags = new TagList { { "result", result } };
        if (keyId != null)
        {
            tags.Add("kid", keyId);
        }

        if (context?.TenantId != null)
        {
            tags.Add("tenant", context.TenantId);
        }

        if (context?.Purpose != null)
        {
            tags.Add("purpose", context.Purpose);
        }

        var service = context?.Service ?? DefaultServiceName;
        if (service != null)
        {
            tags.Add("service", service);
        }

        return tags;
    }

    private sealed record ParsedHeader(
        SecretFileHeader Header,
        byte[] BaseNonce,
        byte[] WrapNonce,
        byte[] WrappedKey,
        byte[] WrapTag,
        byte[] WrapAad,
        byte[] HeaderHash);
}
