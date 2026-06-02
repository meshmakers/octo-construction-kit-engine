using Meshmakers.Octo.ConstructionKit.Contracts;

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

/// <summary>
///     Represents a runtime record, the instance of a construction kit record
/// </summary>
public class RtRecord : RtTypeWithAttributes
{
    /// <summary>
    ///     Creates a new instance of <see cref="RtRecord" />
    /// </summary>
    public RtRecord()
    {
    }

    /// <summary>
    ///     Creates a new instance of <see cref="RtRecord" />
    /// </summary>
    /// <param name="ckRecordId">Construction kit record id</param>
    /// <param name="attributes">List of attributes</param>
    [System.Text.Json.Serialization.JsonConstructor]
    public RtRecord(RtCkId<CkRecordId> ckRecordId, IReadOnlyDictionary<string, object?> attributes)
        : base(attributes)
    {
        CkRecordId = ckRecordId;
    }

    /// <summary>
    ///     Gets or sets the construction kit id
    /// </summary>
    public RtCkId<CkRecordId> CkRecordId { get; set; } = null!;

    /// <inheritdoc />
    protected override string GetLocation()
    {
        return $"{CkRecordId}";
    }
}