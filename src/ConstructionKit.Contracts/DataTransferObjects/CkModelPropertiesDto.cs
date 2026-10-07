using System.Diagnostics;
using YamlDotNet.Serialization;
using System.Text.Json.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
/// Represents the properties of a CK model
/// </summary>
[DebuggerDisplay("{" + nameof(ModelId) + "}")]
public class CkModelPropertiesDto
{
    /// <summary>
    ///     Gets or sets the model id.
    /// </summary>
    [JsonRequired]
    public CkModelId ModelId { get; set; } = null!;
        
    /// <summary>
    ///     An optional description of the model
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Description { get; set; }

    /// <summary>
    ///     CK v2 (AB#5584): the CK language version of the model. <c>null</c> (omitted) means 1. Version 2 enables
    ///     <c>interfaces</c>, <c>implements</c>, attribute <c>access</c> and <c>methods</c>; Phase 0 gives it no
    ///     other semantics. A model with a version above <see cref="MaxSupportedCkLanguage" /> is rejected
    ///     (message 91).
    /// </summary>
    [YamlMember(Alias = "ckLanguage", DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public int? CkLanguage { get; set; }

    /// <summary>
    ///     The highest CK language version this engine understands.
    /// </summary>
    public const int MaxSupportedCkLanguage = 2;

    /// <summary>
    ///     The effective CK language version (<see cref="CkLanguage" /> or 1).
    /// </summary>
    [JsonIgnore]
    [YamlIgnore]
    public int EffectiveCkLanguage => CkLanguage ?? 1;
}