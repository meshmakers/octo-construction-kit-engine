using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     Metadata of a Attribute
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public class CkAttributeMetaDataDto
{
    /// <summary>
    ///     Metadata key
    /// </summary>
    public string Key { get; set; } = null!;

    /// <summary>
    ///     Metadata value
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    ///     An optional description of the attribute
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Description { get; set; }
}