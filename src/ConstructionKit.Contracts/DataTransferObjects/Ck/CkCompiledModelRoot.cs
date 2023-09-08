using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization.Schema;
using YamlDotNet.Serialization;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;

/// <summary>
/// The root object of the compiled version of a CK model.
/// </summary>
[OctoJsonSchema(typeof(CkSchema), nameof(CkSchema.CompiledModelSchema))]
public class CkCompiledModelRoot : CkMetaRootDto
{
    /// <summary>
    /// The URI of the schema for the compiled CK model.
    /// </summary>
    public const string CkCompiledModelSchemaUri = "https://schemas.meshmakers.cloud/construction-kit-compiled.schema.json";
    
    /// <summary>
    /// The URI of the schema for the compiled CK model used for serialization.
    /// </summary>
    [YamlMember(Alias = "$schema")]
    [JsonPropertyName("$schema")]
    public override string SchemaUri { get; } = CkCompiledModelSchemaUri;
    
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
    
    /// <summary>
    /// Returns records of the model that are used for complex attributes
    /// </summary>
    // ReSharper disable once CollectionNeverUpdated.Global
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkRecordDto>? Records { get; set; }
    
    /// <summary>
    /// Returns enums of the model that are used for enum attributes
    /// </summary>
    // ReSharper disable once CollectionNeverUpdated.Global
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkEnumDto>? Enums { get; set; }
}