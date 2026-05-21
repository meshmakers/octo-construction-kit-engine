using System.Text.Json.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;

/// <summary>
/// Build-time inventory written by the BlueprintEmbed MSBuild task and consumed by the
/// BlueprintSourceGenerator. Lists every blueprint version embedded as a resource in the
/// consuming assembly, with the metadata the generator needs to emit IBlueprintEmbeddedSource
/// implementations and the DI extension methods.
/// </summary>
/// <remarks>
/// Schema URI: <c>https://schemas.meshmakers.cloud/blueprints-cache.schema.json</c>.
/// The file format is versioned via the <see cref="Version" /> field so the generator can
/// reject incompatible caches loudly instead of producing silently-broken output.
/// </remarks>
public class BlueprintsCacheDto
{
    /// <summary>
    /// Optional schema URI (mirrored from the file's <c>$schema</c> property).
    /// </summary>
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    /// <summary>
    /// Format version. Currently always <c>"1"</c>.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1";

    /// <summary>
    /// Every blueprint version discovered under one or more <c>&lt;BlueprintFolder&gt;</c> items
    /// in the host project. Order is the discovery order (alphabetical by Name, then ascending
    /// by version).
    /// </summary>
    [JsonPropertyName("blueprints")]
    public List<BlueprintsCacheEntryDto> Blueprints { get; set; } = new();
}

/// <summary>
/// One entry in <see cref="BlueprintsCacheDto.Blueprints" />.
/// </summary>
public class BlueprintsCacheEntryDto
{
    /// <summary>
    /// Blueprint name (without version).
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Blueprint version (Major.Minor.Patch).
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Optional description copied from the manifest. Stored verbatim so the catalog listing
    /// matches what the manifest carries; <c>null</c> when the manifest omits the field.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Dot-separated namespace under which the blueprint's files are embedded.
    /// </summary>
    [JsonPropertyName("resourceNamespace")]
    public string ResourceNamespace { get; set; } = string.Empty;

    /// <summary>
    /// Relative paths of every file embedded for this blueprint (forward-slash separated).
    /// Always contains <c>blueprint.yaml</c>; sibling files like <c>seed-data/entities.yaml</c>
    /// or <c>migrations/from-1.0.0.yaml</c> are listed if present.
    /// </summary>
    [JsonPropertyName("files")]
    public List<string> Files { get; set; } = new();
}
