using System.Diagnostics;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
///     Base class for stream-data entity projections. Mirrors <see cref="RtEntity"/> for the
///     time-series domain — typed properties for the captured attributes plus the built-in stream
///     fields (<see cref="Timestamp"/>, <see cref="RtId"/>, <see cref="CkTypeId"/>,
///     <see cref="RtWellKnownName"/>, <see cref="RtCreationDateTime"/>, <see cref="RtChangedDateTime"/>).
/// </summary>
[DebuggerDisplay("{CkTypeId}@{RtId}@{Timestamp}")]
public class StreamDataEntity : RtTypeWithAttributes
{
    /// <summary>
    ///     Creates a new instance of <see cref="StreamDataEntity"/>.
    /// </summary>
    public StreamDataEntity()
    {
    }

    /// <summary>
    ///     Creates a new instance of <see cref="StreamDataEntity"/> with the attribute bag pre-populated.
    /// </summary>
    public StreamDataEntity(IReadOnlyDictionary<string, object?> attributes)
        : base(attributes)
    {
    }

    /// <summary>
    ///     Runtime id of the entity this data point belongs to.
    /// </summary>
    public OctoObjectId RtId { get; set; }

    /// <summary>
    ///     Construction kit type id of the entity this data point belongs to.
    /// </summary>
    public RtCkId<CkTypeId>? CkTypeId { get; set; }

    /// <summary>
    ///     Timestamp of this data point (time-series position).
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    ///     Well-known name of the source entity, if any.
    /// </summary>
    public string? RtWellKnownName { get; set; }

    /// <summary>
    ///     When the source entity was originally created.
    /// </summary>
    public DateTime? RtCreationDateTime { get; set; }

    /// <summary>
    ///     When the source entity was last modified.
    /// </summary>
    public DateTime? RtChangedDateTime { get; set; }

    /// <inheritdoc />
    protected override string GetLocation()
    {
        return $"{CkTypeId}@{RtId}@{Timestamp:O}";
    }
}
