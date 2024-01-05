using Meshmakers.Octo.Runtime.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.Runtime.Contracts.Serialization;

/// <summary>
///     Event arguments for the <see cref="IRtDeserializeStream.BulkDeserialized" /> event.
/// </summary>
public class RtDeserializeEventArgs : EventArgs
{
    /// <summary>
    ///     Creates a new instance of <see cref="RtDeserializeEventArgs" />.
    /// </summary>
    /// <param name="deserializedEntities"></param>
    public RtDeserializeEventArgs(IEnumerable<RtEntityDto> deserializedEntities)
    {
        DeserializedEntities = deserializedEntities;
    }

    /// <summary>
    ///     Gets the current list of entities. When handled, set <see cref="IsHandled" /> to true.
    /// </summary>
    public IEnumerable<RtEntityDto> DeserializedEntities { get; }

    /// <summary>
    ///     Set to true when the event has been handled.
    /// </summary>
    public bool IsHandled { get; set; }
}