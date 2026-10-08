using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization.Schema;
using YamlDotNet.Serialization;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     The root object of the compiled version of a CK model.
/// </summary>
[OctoJsonSchema(typeof(CkSchema), nameof(CkSchema.GetCompiledModelSchema))]
public class CkCompiledModelRoot : CkModelRootBase
{
    /// <summary>
    ///     The URI of the schema for the compiled CK model.
    /// </summary>
    public const string CkCompiledModelSchemaUri = "https://schemas.meshmakers.cloud/construction-kit-compiled.schema.json";

    /// <summary>
    ///     The URI of the schema for the compiled CK model used for serialization.
    /// </summary>
    [YamlMember(Alias = "$schema")]
    [JsonPropertyName("$schema")]
    public string SchemaUri { get; } = CkCompiledModelSchemaUri;

    /// <summary>
    ///     Gets or sets the dependencies of the model.
    /// </summary>
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public List<CkModelId>? Dependencies { get; set; }

    /// <summary>
    ///     Range-retaining dependencies (CK v2, AB#5664): the declared range plus floor of every direct
    ///     dependency. Only written by a compiler running with range retention
    ///     (<c>OctoCkRangeRetention=true</c>); then references into dependencies are major-qualified
    ///     (<c>System@2/Entity-1</c>) and a resolver accepts any installed version inside the range and at or
    ///     above the floor. <c>null</c> for classic exact-pinned models, whose <see cref="Dependencies" /> keep
    ///     their exact-match semantics. Use <see cref="GetResolutionRanges" /> to resolve either shape.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public List<CkModelDependencyDto>? DependencyRanges { get; set; }

    /// <summary>
    ///     CK v2 (F1.1-S6, AB#5909): the lowest construction kit engine version that can read this model. Written by
    ///     the compiler for <c>ckLanguage: 2</c> and range-retaining output (<c>null</c> = any engine, so v1 output
    ///     is unchanged). An engine refuses to import a model whose value is above its own version (message 126).
    ///     Engines before CK v2 ignore the key; they never see such models because those are published under the
    ///     <c>ck-models/v3/</c> catalog path.
    /// </summary>
    [YamlMember(Alias = "minEngineVersion", DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public string? MinEngineVersion { get; set; }

    /// <summary>
    ///     True when the model was compiled with range retention (<see cref="DependencyRanges" /> is set).
    /// </summary>
    [JsonIgnore]
    [YamlIgnore]
    public bool IsRangeRetaining => DependencyRanges != null;

    /// <summary>
    ///     The ranges a resolver must satisfy for this model's dependencies: the effective ranges of
    ///     <see cref="DependencyRanges" /> for a range-retaining model, otherwise the exact pins of
    ///     <see cref="Dependencies" />.
    /// </summary>
    public IReadOnlyList<CkModelIdVersionRange> GetResolutionRanges()
    {
        if (DependencyRanges != null)
        {
            return DependencyRanges.Select(d => d.GetEffectiveRange()).ToList();
        }

        return Dependencies?.Select(d => d.ToVersionRange()).ToList() ?? [];
    }

    /// <summary>
    ///     Gets or sets the inline migration data for this compiled model.
    ///     When present, allows any service to run CK model migrations without
    ///     needing the CK model NuGet package as a reference.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public CkCompiledMigrationDataDto? Migrations { get; set; }
}
