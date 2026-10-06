using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using Meshmakers.Octo.Runtime.Contracts.Secrets;
using Meshmakers.Octo.Runtime.Engine.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Secrets;

/// <summary>
///     AB#5559: streaming file encryption (format OCTOENC1) with the secret key ring. Keys are generated
///     per test run.
/// </summary>
public class SecretFileProtectorTests
{
    private const int SmallChunk = 4096;

    private static readonly string K1 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static readonly string K2 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    internal static SecretFileProtector Create(Dictionary<string, string>? keys = null, string? activeKeyId = "k1",
        int chunkSize = SmallChunk)
    {
        var options = new SecretEncryptionOptions
        {
            Keys = new Dictionary<string, string>(keys ?? new Dictionary<string, string> { ["k1"] = K1, ["k2"] = K2 },
                StringComparer.OrdinalIgnoreCase),
            ActiveKeyId = activeKeyId
        };
        return new SecretFileProtector(Options.Create(options), NullLogger<SecretFileProtector>.Instance, chunkSize);
    }

    private static async Task<byte[]> ProtectAsync(SecretFileProtector protector, byte[] plaintext,
        SecretFileContext? context = null)
    {
        using var input = new MemoryStream(plaintext);
        using var output = new MemoryStream();
        await protector.ProtectAsync(input, output, context, TestContext.Current.CancellationToken);
        return output.ToArray();
    }

    private static async Task<byte[]> UnprotectAsync(SecretFileProtector protector, byte[] file,
        SecretFileContext? context = null)
    {
        using var input = new MemoryStream(file);
        using var output = new MemoryStream();
        await protector.UnprotectAsync(input, output, context, TestContext.Current.CancellationToken);
        return output.ToArray();
    }

    private static int HeaderLength(SecretFileProtector protector, byte[] file)
    {
        return protector.ReadHeader(new MemoryStream(file)).HeaderLength;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(SmallChunk - 1)]
    [InlineData(SmallChunk)]
    [InlineData(SmallChunk + 1)]
    [InlineData(3 * SmallChunk)]
    [InlineData(10 * SmallChunk + 17)]
    public async Task ProtectThenUnprotect_RoundTrips(int length)
    {
        var protector = Create();
        var plaintext = RandomNumberGenerator.GetBytes(length);

        var file = await ProtectAsync(protector, plaintext);
        var restored = await UnprotectAsync(protector, file);

        Assert.Equal(plaintext, restored);
        // Header + one 16-byte tag per chunk; the final chunk always holds fewer than chunk size bytes.
        var chunks = length / SmallChunk + 1;
        Assert.Equal(HeaderLength(protector, file) + length + chunks * 16, file.Length);
    }

    [Fact]
    public async Task Protect_DoesNotContainPlaintext_AndUsesFreshFileKey()
    {
        var protector = Create();
        var plaintext = "password=hunter2-hunter2-hunter2"u8.ToArray();

        var first = await ProtectAsync(protector, plaintext);
        var second = await ProtectAsync(protector, plaintext);

        Assert.Equal(-1, first.AsSpan().IndexOf(plaintext));
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Protect_WithDefaultChunkSize_RoundTripsMultiChunk()
    {
        var protector = Create(chunkSize: SecretFileFormat.DefaultChunkSize);
        var plaintext = RandomNumberGenerator.GetBytes(2 * SecretFileFormat.DefaultChunkSize + 5);

        var file = await ProtectAsync(protector, plaintext);

        Assert.Equal(SecretFileFormat.DefaultChunkSize, protector.ReadHeader(new MemoryStream(file)).ChunkSize);
        Assert.Equal(plaintext, await UnprotectAsync(protector, file));
    }

    [Fact]
    public async Task ReadHeader_WorksWithoutKeys()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var file = await ProtectAsync(Create(), RandomNumberGenerator.GetBytes(10));
        var withoutKeys = Create(new Dictionary<string, string>(), activeKeyId: null);
        using var stream = new MemoryStream(file);

        var header = withoutKeys.ReadHeader(stream);

        Assert.Equal(1, header.Version);
        Assert.Equal("k1", header.KeyId);
        Assert.Equal(SmallChunk, header.ChunkSize);
        Assert.Equal(DateTimeKind.Utc, header.CreatedAt.Kind);
        Assert.InRange(header.CreatedAt, before, DateTime.UtcNow.AddSeconds(1));
        Assert.Equal(header.HeaderLength, stream.Position);
        Assert.Equal(SecretFileFormat.Magic, System.Text.Encoding.ASCII.GetString(file, 0, 8));
        Assert.False(withoutKeys.IsConfigured);
        Assert.False(withoutKeys.CanUnprotect(header));
    }

    [Fact]
    public async Task CanUnprotect_DependsOnKeyId()
    {
        var file = await ProtectAsync(Create(activeKeyId: "k2"), RandomNumberGenerator.GetBytes(10));
        var header = Create().ReadHeader(new MemoryStream(file));

        Assert.Equal("k2", header.KeyId);
        Assert.True(Create().CanUnprotect(header));
        Assert.True(Create(new Dictionary<string, string> { ["K2"] = K2 }, activeKeyId: null).CanUnprotect(header));
        Assert.False(Create(new Dictionary<string, string> { ["k1"] = K1 }).CanUnprotect(header));
    }

    [Fact]
    public async Task Unprotect_UnknownKeyId_Throws()
    {
        var file = await ProtectAsync(Create(activeKeyId: "k2"), RandomNumberGenerator.GetBytes(10));

        var ex = await Assert.ThrowsAsync<UnknownSecretKeyIdException>(() =>
            UnprotectAsync(Create(new Dictionary<string, string> { ["k1"] = K1 }), file));
        Assert.Equal("k2", ex.KeyId);
    }

    [Fact]
    public async Task Unprotect_WithoutAnyKey_ThrowsNotConfigured()
    {
        var file = await ProtectAsync(Create(), RandomNumberGenerator.GetBytes(10));

        await Assert.ThrowsAsync<SecretEncryptionNotConfiguredException>(() =>
            UnprotectAsync(Create(new Dictionary<string, string>(), activeKeyId: null), file));
    }

    [Fact]
    public async Task Protect_WithoutKeyRing_ThrowsNotConfigured()
    {
        var protector = Create(new Dictionary<string, string>(), activeKeyId: null);

        Assert.False(protector.IsConfigured);
        await Assert.ThrowsAsync<SecretEncryptionNotConfiguredException>(() =>
            ProtectAsync(protector, RandomNumberGenerator.GetBytes(10)));
    }

    [Fact]
    public async Task Protect_WithKeysButNoActiveKey_ThrowsNotConfigured()
    {
        await Assert.ThrowsAsync<SecretEncryptionNotConfiguredException>(() =>
            ProtectAsync(Create(activeKeyId: null), RandomNumberGenerator.GetBytes(10)));
    }

    [Fact]
    public async Task Unprotect_SameKeyUnderOtherKeyId_FailsWrap()
    {
        // The file key is wrapped under a key derived from the ring key of the header's kid; renaming the
        // kid in the header (same length) must not work even when that kid exists.
        var protector = Create();
        var file = await ProtectAsync(protector, RandomNumberGenerator.GetBytes(100));
        file[11] = (byte)'2'; // "k1" -> "k2"

        Assert.Equal("k2", protector.ReadHeader(new MemoryStream(file)).KeyId);
        await Assert.ThrowsAsync<InvalidSecretFileException>(() => UnprotectAsync(protector, file));
    }

    [Theory]
    [InlineData(12)] // createdAt
    [InlineData(23)] // chunk size (low byte, still in range)
    [InlineData(24)] // base nonce
    [InlineData(36)] // wrap nonce
    [InlineData(48)] // wrapped key
    [InlineData(80)] // wrap tag
    public async Task Unprotect_TamperedHeader_Throws(int offset)
    {
        var protector = Create();
        var file = await ProtectAsync(protector, RandomNumberGenerator.GetBytes(3 * SmallChunk));
        file[offset] ^= 0x01;

        await Assert.ThrowsAsync<InvalidSecretFileException>(() => UnprotectAsync(protector, file));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Unprotect_InvalidPrefix_Throws(int offset)
    {
        var protector = Create();
        var file = await ProtectAsync(protector, RandomNumberGenerator.GetBytes(10));
        file[offset] ^= 0x40;

        Assert.Throws<InvalidSecretFileException>(() => protector.ReadHeader(new MemoryStream(file)));
        await Assert.ThrowsAsync<InvalidSecretFileException>(() => UnprotectAsync(protector, file));
    }

    [Fact]
    public async Task Unprotect_UnsupportedVersion_Throws()
    {
        var protector = Create();
        var file = await ProtectAsync(protector, RandomNumberGenerator.GetBytes(10));
        file[8] = 2;

        var ex = Assert.Throws<InvalidSecretFileException>(() => protector.ReadHeader(new MemoryStream(file)));
        Assert.Contains("version", ex.Message);
    }

    [Fact]
    public void ReadHeader_NotAFile_Throws()
    {
        var protector = Create();

        Assert.Throws<InvalidSecretFileException>(() => protector.ReadHeader(new MemoryStream("PK\u0003\u0004"u8.ToArray())));
        Assert.Throws<InvalidSecretFileException>(() => protector.ReadHeader(new MemoryStream()));
    }

    [Theory]
    [InlineData(0)] // first chunk
    [InlineData(1)] // middle chunk
    [InlineData(3)] // final (short) chunk
    public async Task Unprotect_TamperedChunk_Throws(int chunk)
    {
        var protector = Create();
        var file = await ProtectAsync(protector, RandomNumberGenerator.GetBytes(3 * SmallChunk + 50));
        var offset = HeaderLength(protector, file) + chunk * (SmallChunk + 16) + 7;
        file[offset] ^= 0x80;

        await Assert.ThrowsAsync<InvalidSecretFileException>(() => UnprotectAsync(protector, file));
    }

    [Fact]
    public async Task Unprotect_TamperedTag_Throws()
    {
        var protector = Create();
        var file = await ProtectAsync(protector, RandomNumberGenerator.GetBytes(SmallChunk + 50));
        file[^1] ^= 0x01;

        await Assert.ThrowsAsync<InvalidSecretFileException>(() => UnprotectAsync(protector, file));
    }

    [Theory]
    [InlineData(1)] // inside the final chunk
    [InlineData(66)] // the whole final chunk (50 + 16): ends at a chunk boundary
    [InlineData(67)] // into the previous full chunk
    [InlineData(66 + SmallChunk + 16)] // two whole chunks
    public async Task Unprotect_Truncated_Throws(int cutBytes)
    {
        var protector = Create();
        var file = await ProtectAsync(protector, RandomNumberGenerator.GetBytes(3 * SmallChunk + 50));

        await Assert.ThrowsAsync<InvalidSecretFileException>(() =>
            UnprotectAsync(protector, file[..^cutBytes]));
    }

    [Fact]
    public async Task Unprotect_TruncatedAtFullChunkBoundary_WhenPlaintextIsMultipleOfChunk_Throws()
    {
        // Plaintext = 2 full chunks: the file ends with an empty final chunk (tag only). Dropping it leaves a
        // file that ends exactly after a full chunk.
        var protector = Create();
        var file = await ProtectAsync(protector, RandomNumberGenerator.GetBytes(2 * SmallChunk));

        await Assert.ThrowsAsync<InvalidSecretFileException>(() => UnprotectAsync(protector, file[..^16]));
    }

    [Fact]
    public async Task Unprotect_HeaderOnly_Throws()
    {
        var protector = Create();
        var file = await ProtectAsync(protector, RandomNumberGenerator.GetBytes(10));

        await Assert.ThrowsAsync<InvalidSecretFileException>(() =>
            UnprotectAsync(protector, file[..HeaderLength(protector, file)]));
        await Assert.ThrowsAsync<InvalidSecretFileException>(() => UnprotectAsync(protector, file[..20]));
    }

    [Fact]
    public async Task Unprotect_ReorderedChunks_Throws()
    {
        var protector = Create();
        var file = await ProtectAsync(protector, RandomNumberGenerator.GetBytes(3 * SmallChunk + 50));
        var start = HeaderLength(protector, file);
        var size = SmallChunk + 16;
        var chunk0 = file.AsSpan(start, size).ToArray();
        file.AsSpan(start + size, size).CopyTo(file.AsSpan(start, size));
        chunk0.CopyTo(file.AsSpan(start + size, size));

        await Assert.ThrowsAsync<InvalidSecretFileException>(() => UnprotectAsync(protector, file));
    }

    [Fact]
    public async Task Unprotect_ChunkFromOtherFile_Throws()
    {
        var protector = Create();
        var a = await ProtectAsync(protector, RandomNumberGenerator.GetBytes(2 * SmallChunk + 5));
        var b = await ProtectAsync(protector, RandomNumberGenerator.GetBytes(2 * SmallChunk + 5));
        var start = HeaderLength(protector, a);
        b.AsSpan(start, SmallChunk + 16).CopyTo(a.AsSpan(start, SmallChunk + 16));

        await Assert.ThrowsAsync<InvalidSecretFileException>(() => UnprotectAsync(protector, a));
    }

    [Fact]
    public async Task Unprotect_TrailingData_Throws()
    {
        var protector = Create();
        var file = await ProtectAsync(protector, RandomNumberGenerator.GetBytes(100));

        await Assert.ThrowsAsync<InvalidSecretFileException>(() =>
            UnprotectAsync(protector, [.. file, 0x00]));
    }

    [Fact]
    public async Task ProtectAndUnprotect_AreCounted()
    {
        var purpose = "test-" + Guid.NewGuid().ToString("N");
        var measurements = new List<(string Instrument, string? Result)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == SecretDiagnostics.MeterName &&
                instrument.Name.StartsWith("octo.secrets.file_", StringComparison.Ordinal))
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            string? result = null;
            var matches = false;
            foreach (var tag in tags)
            {
                matches |= tag is { Key: "purpose" } && Equals(tag.Value, purpose);
                if (tag.Key == "result")
                {
                    result = tag.Value as string;
                }
            }

            if (matches)
            {
                lock (measurements)
                {
                    measurements.Add((instrument.Name, result));
                }
            }
        });
        listener.Start();

        var context = new SecretFileContext("tenant1", purpose);
        var protector = Create();
        var file = await ProtectAsync(protector, RandomNumberGenerator.GetBytes(10), context);
        await UnprotectAsync(protector, file, context);
        await Assert.ThrowsAsync<UnknownSecretKeyIdException>(() =>
            UnprotectAsync(Create(new Dictionary<string, string> { ["k2"] = K2 }, "k2"), file, context));

        Assert.Equal(
        [
            ("octo.secrets.file_protect", "ok"),
            ("octo.secrets.file_unprotect", "ok"),
            ("octo.secrets.file_unprotect", "unknown_key_id")
        ], measurements);
    }

    [Fact]
    public void Constructor_RejectsChunkSizeOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(chunkSize: SecretFileFormat.MinChunkSize - 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(chunkSize: SecretFileFormat.MaxChunkSize + 1));
    }

    [Fact]
    public void AddRuntimeEngine_RegistersFileProtector()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddRuntimeEngine();
        using var provider = services.BuildServiceProvider();

        var protector = provider.GetRequiredService<ISecretFileProtector>();
        Assert.IsType<SecretFileProtector>(protector);
        Assert.Same(protector, provider.GetRequiredService<ISecretFileProtector>());
        Assert.False(protector.IsConfigured);
    }
}

/// <summary>
///     Large streamed round trip with bounded allocations. Not parallelized, because it measures the
///     process-wide allocation counter.
/// </summary>
[Collection(nameof(SecretFileProtectorStreamingCollection))]
public class SecretFileProtectorStreamingTests
{
    [Fact]
    public async Task LargeFile_StreamsWithConstantMemory()
    {
        const long length = 128L * 1024 * 1024 + 12345;
        var ct = TestContext.Current.CancellationToken;
        var protector = SecretFileProtectorTests.Create(chunkSize: SecretFileFormat.DefaultChunkSize);
        var path = Path.Combine(Path.GetTempPath(), $"octoenc-test-{Guid.NewGuid():N}{SecretFileFormat.FileExtension}");
        try
        {
            byte[] sourceHash;
            byte[] restoredHash;
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);

            await using (var source = new PatternStream(length))
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                             FileOptions.Asynchronous))
            {
                await protector.ProtectAsync(source, file, cancellationToken: ct);
                sourceHash = source.Hash();
            }

            using (var sink = new HashingSink())
            {
                await using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                                 FileOptions.Asynchronous))
                {
                    await protector.UnprotectAsync(file, sink, cancellationToken: ct);
                }

                Assert.Equal(length, sink.Length);
                restoredHash = sink.Hash();
            }

            var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            Assert.Equal(sourceHash, restoredHash);
            Assert.Equal(length + (length / SecretFileFormat.DefaultChunkSize + 1) * 16 + 96,
                new FileInfo(path).Length);
            // 256 MiB passed through both directions; buffers are a few chunks. Generous bound for noise.
            Assert.True(allocated < 32L * 1024 * 1024, $"Allocated {allocated} bytes for a {length} byte file.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    ///     Non-seekable deterministic source that hashes what it hands out.
    /// </summary>
    private sealed class PatternStream(long length) : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private long _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public byte[] Hash() => _hash.GetHashAndReset();

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var n = (int)Math.Min(buffer.Length, length - _position);
            for (var i = 0; i < n; i++)
            {
                var p = _position + i;
                buffer[i] = (byte)(p * 31 ^ (p >> 11));
            }

            _position += n;
            _hash.AppendData(buffer[..n]);
            return n;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Read(buffer.Span));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Task.FromResult(Read(buffer.AsSpan(offset, count)));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hash.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    ///     Non-seekable sink that only hashes.
    /// </summary>
    private sealed class HashingSink : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        private long _length;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _length;

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public byte[] Hash() => _hash.GetHashAndReset();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _hash.AppendData(buffer);
            _length += buffer.Length;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hash.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

/// <summary>
///     Runs the streaming test alone (allocation measurement).
/// </summary>
[CollectionDefinition(nameof(SecretFileProtectorStreamingCollection), DisableParallelization = true)]
public class SecretFileProtectorStreamingCollection;
