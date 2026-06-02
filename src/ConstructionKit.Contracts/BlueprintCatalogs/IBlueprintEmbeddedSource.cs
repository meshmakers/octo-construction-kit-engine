using System.Reflection;

namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

/// <summary>
/// Discovery hook for a single embedded blueprint version. Implementations are produced by the
/// blueprint source generator and registered as DI services (one per blueprint version shipped in
/// a NuGet package). The engine's <c>EmbeddedResourceBlueprintCatalog</c> consumes the registered
/// set at runtime to enumerate available blueprints and to open files inside them via the
/// assembly's manifest-resource stream API.
/// </summary>
/// <remarks>
/// This is the blueprint analogue of <c>ICkEmbeddedMigrationSource</c> for CK-model migrations: a
/// minimal Assembly + ResourceNamespace pointer rather than the full deserialised blueprint, so the
/// catalog can lazy-load on demand and the DTO contract stays in <c>ConstructionKit.Contracts</c>.
/// </remarks>
public interface IBlueprintEmbeddedSource
{
    /// <summary>
    /// Identifier of the embedded blueprint, including version. Stamped into the generated source by
    /// the blueprint generator from the YAML manifest's <c>blueprintId</c> field.
    /// </summary>
    BlueprintId BlueprintId { get; }

    /// <summary>
    /// Short description of the blueprint, copied from the manifest's <c>description</c> field. Used
    /// for catalog listings (Studio UI, <c>octo-cli -c ListBlueprints</c>) without forcing a manifest
    /// deserialisation roundtrip.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Assembly that carries the embedded resources for this blueprint.
    /// </summary>
    Assembly Assembly { get; }

    /// <summary>
    /// Base resource namespace for the embedded blueprint files, e.g.
    /// <c>Meshmakers.Octo.ConstructionKit.Models.System.Communication.Blueprints.Communication-1.0.0</c>.
    /// The catalog appends <c>.blueprint.yaml</c> for the manifest and
    /// <c>.{relativePath-with-slashes-as-dots}</c> for sibling files.
    /// </summary>
    string ResourceNamespace { get; }
}
