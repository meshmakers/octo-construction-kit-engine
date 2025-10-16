using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

// ReSharper disable CollectionNeverQueried.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;

/// <summary>
///     Defines a record
/// </summary>
public class RtRecordTcDto : RtTypeWithAttributesTcDto
{
    /// <summary>
    ///     Creates a new instance of <see cref="RtRecordTcDto" />
    /// </summary>
    public RtRecordTcDto()
    {
    }

    /// <summary>
    ///     Gets or sets the ck type id of the record
    /// </summary>
    [System.Text.Json.Serialization.JsonRequired]
    [System.Text.Json.Serialization.JsonConverter(typeof(RtCkIdTypeIdConverter))]
    public RtCkId<CkRecordId> CkRecordId { get; set; } = null!;
}