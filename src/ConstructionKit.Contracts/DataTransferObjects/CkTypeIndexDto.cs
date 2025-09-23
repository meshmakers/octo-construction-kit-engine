using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     Defines an index for a CK type.
/// </summary>
public class CkTypeIndexDto
{
    /// <summary>
    ///     Creates a new instance of the <see cref="CkTypeIndexDto" /> class.
    /// </summary>
    public CkTypeIndexDto()
    {
        Fields = [];
    }

    /// <summary>
    ///     Get or sets the index type.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IndexTypeDto IndexType { get; set; }

    /// <summary>
    ///     Gets or sets the language of the index.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Language { get; set; }

    /// <summary>
    ///     Gets or sets the fields of the index.
    /// </summary>
    public List<CkIndexFieldsDto> Fields { get; set; }
}