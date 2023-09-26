using Meshmakers.Octo.ConstructionKit.Contracts;

// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable UnusedMember.Global

namespace Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

/// <summary>
/// Represents the runtime entity, the instance of a construction kit type.
/// </summary>
public class RtEntity : RtTypeWithAttributes
{
    /// <summary>
    ///     Gets or sets the runtime id
    /// </summary>
    public OctoObjectId RtId { get; set; }

    /// <summary>
    ///     Returns the creation date time
    /// </summary>
    public DateTime? RtCreationDateTime { get; set; }

    /// <summary>
    ///     Returns the last change date time
    /// </summary>
    public DateTime? RtChangedDateTime { get; set; }

    /// <summary>
    ///     Gets or sets the construction kit id
    /// </summary>
    public CkId<CkTypeId> CkTypeId { get; set; }

    /// <summary>
    ///     Returns the well known name to access well known entities in a faster way
    /// </summary>
    public string? RtWellKnownName { get; set; }

    /// <inheritdoc />
    protected override string GetLocation()
    {
        return $"{CkTypeId}.{RtId}";
    }
}