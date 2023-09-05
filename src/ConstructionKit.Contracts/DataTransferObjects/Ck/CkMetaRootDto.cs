using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization.Schema;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;

/// <summary>
/// Represents the content of the metadata file
/// </summary>
[OctoJsonSchema(typeof(CkSchema), nameof(CkSchema.MetaSchema))]
public class CkMetaRootDto
{
    /// <summary>
    /// The URI of the schema for the CK meta.
    /// </summary>
    public const string CkMetaSchemaUri = "https://schemas.meshmakers.cloud/construction-kit-meta.schema.json";

    /// <summary>
    /// The URI of the schema for the CK meta used for serialization.
    /// </summary>
    [YamlMember(Alias = "$schema")]
    [JsonPropertyName("$schema")]
    public virtual string SchemaUri { get; } = CkMetaSchemaUri;
    
    /// <summary>
    /// Creates a new instance of the <see cref="CkMetaRootDto"/> class.
    /// </summary>
    public CkMetaRootDto()
    {
        Dependencies = new List<CkModelId>();
    }
    
    /// <summary>
    /// Gets or sets the model id.
    /// </summary>
    [JsonRequired]
    public CkModelId ModelId { get; set; }

    /// <summary>
    /// Gets or sets the dependencies of the model.
    /// </summary>
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public List<CkModelId>? Dependencies { get; set; }
}