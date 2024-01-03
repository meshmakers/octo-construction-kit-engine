using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
/// Compiled version of a CK type.
/// </summary>
public class CkCompiledTypeDto : CkTypeDto
{
    /// <summary>
    /// Gets or sets a value indicating whether this type is a collection root.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool IsCollectionRoot { get; set; }
}