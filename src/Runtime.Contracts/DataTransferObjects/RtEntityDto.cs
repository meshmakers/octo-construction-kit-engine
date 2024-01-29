using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Newtonsoft.Json;
using YamlDotNet.Serialization;

// ReSharper disable CollectionNeverQueried.Global

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.Runtime.Contracts.DataTransferObjects;

/// <summary>
///     Defines an entity
/// </summary>
public class RtEntityDto
{
    /// <summary>
    ///     Creates a new instance of <see cref="RtEntityDto" />
    /// </summary>
    public RtEntityDto()
    {
        Attributes = new List<RtAttributeDto>();
    }

    /// <summary>
    ///     Gets or sets the id of the entity
    /// </summary>
    [System.Text.Json.Serialization.JsonRequired]
    [System.Text.Json.Serialization.JsonConverter(typeof(OctoObjectIdConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(NewtonOctoObjectIdConverter))]
    public OctoObjectId RtId { get; set; }

    /// <summary>
    ///     Returns the creation date time
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public DateTime? RtCreationDateTime { get; set; }

    /// <summary>
    ///     Returns the last change date time
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public DateTime? RtChangedDateTime { get; set; }

    /// <summary>
    ///     Gets or sets the ck type id of the entity
    /// </summary>
    [System.Text.Json.Serialization.JsonRequired]
    [System.Text.Json.Serialization.JsonConverter(typeof(CkIdTypeIdConverter))]
    public CkId<CkTypeId> CkTypeId { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the well known name of the entity
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? RtWellKnownName { get; set; }

    /// <summary>
    ///     Gets or sets the attributes of the entity
    /// </summary>
    public List<RtAttributeDto> Attributes { get; set; }

    /// <summary>
    ///     Gets or sets the associations of the entity
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<RtAssociationDto>? Associations { get; set; }
}