using System.Runtime.CompilerServices;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;

namespace Meshmakers.Octo.Runtime.Contracts.Serialization;

/// <summary>
///     Interface of a stream that contains deserialized data.
/// </summary>
public interface IRtDeserializeStream : IDisposable
{
    /// <summary>
    ///     Returns dependencies of the model. Supports version ranges.
    /// </summary>
    public IReadOnlyCollection<CkModelIdVersionRange> Dependencies { get; }

    /// <summary>
    ///     Indicates that a bulk of entities has been deserialized. The event is raised synchronously by
    ///     <see cref="ReadAsync" />; a handler must finish its work before it returns. Never subscribe an <c>async</c>
    ///     handler (an <c>async void</c> lambda): <see cref="ReadAsync" /> would not wait for it and its exceptions would be
    ///     lost (AB#8325). Use <see cref="ReadBulksAsync" /> to process a bulk asynchronously.
    /// </summary>
    event EventHandler<RtDeserializeEventArgs>? BulkDeserialized;

    /// <summary>
    ///     Reads the entities array of the stream and returns the entities as event in <see cref="BulkDeserialized" />.
    ///     Every entity is handed out exactly once.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that completes when the whole array was read and every event handler returned.</returns>
    Task ReadAsync(CancellationToken? cancellationToken = null);

    /// <summary>
    ///     Reads the entities array of the stream and returns the entities in bulks, in file order; every entity is handed out
    ///     exactly once. The next bulk is read only when the consumer asks for it, so a consumer that awaits its work per bulk
    ///     (<c>await foreach</c>) processes the file in sequence and sees every exception of that work (AB#8325).
    /// </summary>
    /// <remarks>
    ///     The default implementation keeps existing implementers of this interface compatible: it reads the whole stream
    ///     through <see cref="ReadAsync" /> and <see cref="BulkDeserialized" /> first and then hands the collected bulks out,
    ///     so it holds all entities in memory. Implementers should override it to stream.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The bulks of deserialized entities.</returns>
    async IAsyncEnumerable<IReadOnlyList<RtEntityTcDto>> ReadBulksAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var bulks = new List<IReadOnlyList<RtEntityTcDto>>();

        void Collect(object? sender, RtDeserializeEventArgs args)
        {
            bulks.Add(args.DeserializedEntities.ToList());
            args.IsHandled = true;
        }

        BulkDeserialized += Collect;
        try
        {
            await ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            BulkDeserialized -= Collect;
        }

        foreach (var bulk in bulks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return bulk;
        }
    }
}