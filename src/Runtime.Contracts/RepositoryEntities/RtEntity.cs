using System.Diagnostics;
using Meshmakers.Octo.ConstructionKit.Contracts;

// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable UnusedMember.Global

namespace Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

/// <summary>
///     Represents the runtime entity, the instance of a construction kit type.
/// </summary>
[DebuggerDisplay("{CkTypeId}@{RtId}")]
public class RtEntity : RtTypeWithAttributes
{
    /// <summary>
    ///     Creates a new instance of <see cref="RtEntity" />
    /// </summary>
    public RtEntity()
    {
    }

    /// <summary>
    ///     Creates a new instance of <see cref="RtEntity" />
    /// </summary>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="rtId">Object id</param>
    public RtEntity(RtCkId<CkTypeId> ckTypeId, OctoObjectId rtId)
    {
        CkTypeId = ckTypeId;
        RtId = rtId;
    }

    /// <summary>
    ///     Creates a new instance of <see cref="RtEntity" />
    /// </summary>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="rtId">Object id</param>
    /// <param name="attributes">List of attributes</param>
    [System.Text.Json.Serialization.JsonConstructor]
    public RtEntity(RtCkId<CkTypeId> ckTypeId, OctoObjectId rtId, IReadOnlyDictionary<string, object?> attributes)
        : base(attributes)
    {
        CkTypeId = ckTypeId;
        RtId = rtId;
    }

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
    ///     Returns the time the entity was archived
    /// </summary>
    public DateTime? RtArchivedDateTime { get; set; }

    /// <summary>
    ///     Gets or sets the construction kit id
    /// </summary>
    public RtCkId<CkTypeId>? CkTypeId { get; set; }

    /// <summary>
    ///     Returns the well-known name to access well-known entities in a faster way
    /// </summary>
    public string? RtWellKnownName { get; set; }

    /// <summary>
    ///     Engine-computed display name, evaluated from the CK type's displayNameRule on save.
    ///     Read-only for API consumers; null when the type has no rule or every referenced
    ///     attribute is empty (readers fall back to "ckTypeId@rtId").
    /// </summary>
    public string? RtDisplayName { get; set; }

    /// <summary>
    ///     Engine-computed display description, evaluated from the CK type's displayDescriptionRule
    ///     on save. Read-only for API consumers.
    /// </summary>
    public string? RtDisplayDescription { get; set; }
    
    /// <summary>
    ///     Gets or sets the runtime version, which is used to detect changes of the entity
    /// </summary>
    public ulong RtVersion { get; set; }
    
    /// <summary>
    ///     Gets or sets the state of the entity
    /// </summary>
    public RtState? RtState { get; set; }

    /// <inheritdoc />
    protected override string GetLocation()
    {
        return $"{CkTypeId}@{RtId}";
    }
}