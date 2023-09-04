using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization.Schema;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
/// A part of a CK model.
/// </summary>
[OctoJsonSchema(typeof(CkSchema), nameof(CkSchema.ElementsSchema))]
public class CkElementsRootDto
{
    /// <summary>
    /// The URI of the schema for the CK elements.
    /// </summary>
    public const string CkElementsSchemaUri = "https://schemas.meshmakers.cloud/construction-kit-elements.schema.json";

    /// <summary>
    /// The URI of the schema for the CK elements used for serialization.
    /// </summary>
    [YamlMember(Alias = "$schema")]
    [JsonPropertyName("$schema")]
    public string SchemaUri { get; } = CkElementsSchemaUri;
    
    /// <summary>
    /// Returns types of the model
    /// </summary>
    // ReSharper disable once CollectionNeverUpdated.Global
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkTypeDto>? Types { get; set; }

    /// <summary>
    /// Returns associations of the model
    /// </summary>
    // ReSharper disable once CollectionNeverUpdated.Global
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkAssociationRoleDto>? AssociationRoles { get; set; }

    /// <summary>
    /// Returns attributes of the model
    /// </summary>
    // ReSharper disable once CollectionNeverUpdated.Global
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkAttributeDto>? Attributes { get; set; }
}