using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization.Schema;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     Represents the content of a model configuration file
/// </summary>
[OctoJsonSchema(typeof(CkSchema), nameof(CkSchema.GetModelConfigSchema))]
public class CkModelConfigDto
{
    /// <summary>
    ///     The URI of the schema for the CK meta.
    /// </summary>
    public const string CkMetaSchemaUri = "https://schemas.meshmakers.cloud/construction-kit-meta.schema.json";

    /// <summary>
    ///     Creates a new instance of the <see cref="CkModelConfigDto" /> class.
    /// </summary>
    public CkModelConfigDto()
    {
        Imports = new List<CkModelId>();
    }

    /// <summary>
    ///     The URI of the schema for the CK meta used for serialization.
    /// </summary>
    [YamlMember(Alias = "$schema")]
    [JsonPropertyName("$schema")]
    public virtual string SchemaUri { get; } = CkMetaSchemaUri;

    /// <summary>
    ///     Gets or sets the imports of the model.
    /// </summary>
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public List<CkModelId>? Imports { get; set; }
}