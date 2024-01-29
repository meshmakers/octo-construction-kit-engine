using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using YamlDotNet.Serialization;

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.Runtime.Contracts.DataTransferObjects;

/// <summary>
///     Defines an attribute of an entity
/// </summary>
public class RtAttributeDto
{
    /// <summary>
    ///     Gets or sets the id of the attribute.
    /// </summary>
    [JsonRequired]
    [JsonConverter(typeof(CkIdAttributeIdConverter))]
    public CkId<CkAttributeId> Id { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the value of the attribute.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public object? Value { get; set; }
}