using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization.Schema;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
/// Represents the content of the metadata file
/// </summary>
[OctoJsonSchema(typeof(CkSchema), nameof(CkSchema.MetaSchema))]
public class CkMetaRootDto
{
    public const string CkMetaSchemaUri = "https://schemas.meshmakers.cloud/construction-kit-meta.schema.json";

    [YamlMember(Alias = "$schema")]
    [JsonPropertyName("$schema")]
    public virtual string SchemaUri { get; } = CkMetaSchemaUri;
    
    public CkMetaRootDto()
    {
        Dependencies = new List<CkModelId>();
    }
    
    [JsonRequired]
    public CkModelId ModelId { get; set; }

    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public List<CkModelId>? Dependencies { get; set; }
}