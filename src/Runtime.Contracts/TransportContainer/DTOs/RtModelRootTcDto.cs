using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Contracts.Serialization.Schema;
using YamlDotNet.Serialization;

// ReSharper disable CollectionNeverQueried.Global
// ReSharper disable CollectionNeverUpdated.Global

namespace Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;

/// <summary>
///     Defines an entity in the runtime model
/// </summary>
[OctoJsonSchema(typeof(RtSchema), nameof(RtSchema.GetRuntimeSchema))]
public class RtModelRootTcDto
{
    /// <summary>
    ///     The URI of the schema for the CK meta.
    /// </summary>
    public const string RtSchemaUri = "https://schemas.meshmakers.cloud/runtime-model.schema.json";

    /// <summary>
    ///     Creates a new instance of <see cref="RtModelRootTcDto" />.
    /// </summary>
    public RtModelRootTcDto()
    {
        Dependencies = [];
        Entities = [];
    }

    /// <summary>
    ///     The URI of the schema for the CK meta used for serialization.
    /// </summary>
    [YamlMember(Alias = "$schema")]
    [JsonPropertyName("$schema")]
    public virtual string SchemaUri { get; } = RtSchemaUri;

    /// <summary>
    ///     Gets or sets the dependencies of the model. Supports version ranges (e.g. "Basic-[2.0,3.0)").
    /// </summary>
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public List<CkModelIdVersionRange> Dependencies { get; set; }

    /// <summary>
    ///     Gets a list of entities in the runtime model.
    /// </summary>
    public List<RtEntityTcDto> Entities { get; set; }
}