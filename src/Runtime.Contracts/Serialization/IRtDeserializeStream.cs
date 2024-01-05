using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.Serialization;

/// <summary>
///     Interface of a stream that contains deserialized data.
/// </summary>
public interface IRtDeserializeStream : IDisposable
{
    /// <summary>
    ///     Returns dependencies of the model.
    /// </summary>
    public IReadOnlyCollection<CkModelId> Dependencies { get; }

    /// <summary>
    ///     Indicates that a bulk of entities has been deserialized.
    /// </summary>
    event EventHandler<RtDeserializeEventArgs>? BulkDeserialized;

    /// <summary>
    ///     Reads the entities array of the stream and returns the entities as event in <see cref="BulkDeserialized" />.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task ReadAsync(CancellationToken? cancellationToken = null);
}