using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

// ReSharper disable CollectionNeverQueried.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.Runtime.Contracts.DataTransferObjects;

/// <summary>
///     Defines a record
/// </summary>
public class RtRecordDto
{
    /// <summary>
    ///     Creates a new instance of <see cref="RtRecordDto" />
    /// </summary>
    public RtRecordDto()
    {
        Attributes = new List<RtAttributeDto>();
    }

    /// <summary>
    ///     Gets or sets the ck type id of the record
    /// </summary>
    [System.Text.Json.Serialization.JsonRequired]
    [System.Text.Json.Serialization.JsonConverter(typeof(CkIdTypeIdConverter))]
    public CkId<CkRecordId> CkRecordId { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the attributes of the record
    /// </summary>
    public List<RtAttributeDto> Attributes { get; set; }
}