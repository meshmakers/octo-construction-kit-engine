using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     Defines index fields in the database for a ck type.
/// </summary>
public class CkIndexFieldsDto
{
    /// <summary>
    ///     The weight of the index. The higher the weight, the more important the index is.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public int? Weight { get; set; }

    /// <summary>
    ///     A list of attribute paths that are used to create the index.
    /// </summary>
    [JsonRequired]
    public List<string> AttributePaths { get; set; } = null!;
}