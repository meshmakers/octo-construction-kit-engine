using System.Runtime.CompilerServices;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Exchange;

/// <summary>
///     Test double of a JSON entity stream: hands out the given bulks in order, through the event of
///     <see cref="ReadAsync" /> or as the bulks of <see cref="ReadBulksAsync" />. <see cref="ReadBulksAsync" /> yields
///     the thread before each bulk, as the real stream does while it reads the file.
/// </summary>
internal sealed class FakeRtDeserializeStream(RtEntityTcDto[][] bulks) : IRtDeserializeStream
{
    public IReadOnlyCollection<CkModelIdVersionRange> Dependencies { get; } = [];

    public event EventHandler<RtDeserializeEventArgs>? BulkDeserialized;

    public Task ReadAsync(CancellationToken? cancellationToken = null)
    {
        foreach (var bulk in bulks)
        {
            BulkDeserialized?.Invoke(this, new RtDeserializeEventArgs(bulk));
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<IReadOnlyList<RtEntityTcDto>> ReadBulksAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var bulk in bulks)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return bulk;
        }
    }

    public void Dispose()
    {
    }
}
