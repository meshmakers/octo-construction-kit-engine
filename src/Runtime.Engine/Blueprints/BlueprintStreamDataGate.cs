using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
/// A blueprint that carries stream-data archives depends on the CK model <c>System.StreamData</c>. That model is
/// service-managed: it is installed by <c>EnableStreamData</c> and by nothing else, and the catalogs offer only recent
/// versions of it (not the floor a blueprint declares). On a tenant without stream data the dependency can therefore
/// neither be satisfied nor installed, and resolving it used to fail the whole apply (AB#7991).
/// </summary>
/// <remarks>
/// With stream data off the engine takes the stream-data part out of the apply instead: the <c>System.StreamData</c>
/// dependency is not installed or validated, and the seed entities of that model (the archives) are not imported. The rest
/// of the blueprint is applied as usual and the result reports each left-out archive and one warning.
/// </remarks>
internal static class BlueprintStreamDataGate
{
    /// <summary>Name of the service-managed stream-data CK model.</summary>
    internal const string ModelName = "System.StreamData";

    /// <summary><c>true</c> when <paramref name="modelName" /> is the stream-data CK model (version suffix ignored).</summary>
    internal static bool IsStreamDataModel(string? modelName)
    {
        if (string.IsNullOrEmpty(modelName))
        {
            return false;
        }

        var dash = modelName.IndexOf('-', StringComparison.Ordinal);
        var name = dash < 0 ? modelName : modelName[..dash];
        return string.Equals(name, ModelName, StringComparison.Ordinal);
    }

    /// <summary><c>true</c> when a dependency or an entity belongs to the stream-data CK model.</summary>
    internal static bool ContainsStreamData(
        IEnumerable<CkModelIdVersionRange> dependencies, IEnumerable<RtEntityTcDto> entities) =>
        dependencies.Any(d => IsStreamDataModel(d.Name)) || entities.Any(e => IsStreamDataModel(e.CkTypeId?.ModelId));

    /// <summary>The dependencies without the stream-data CK model.</summary>
    internal static List<CkModelIdVersionRange> WithoutStreamDataModel(IEnumerable<CkModelIdVersionRange> dependencies) =>
        dependencies.Where(d => !IsStreamDataModel(d.Name)).ToList();

    /// <summary>
    /// Removes the entities of the stream-data CK model from <paramref name="entities" /> and returns one candidate per
    /// removed entity that has an rtId, flagged <see cref="BlueprintArchiveCandidate.NotImported" />, so the result lists
    /// what was left out.
    /// </summary>
    internal static List<BlueprintArchiveCandidate> RemoveStreamDataEntities(
        List<RtEntityTcDto> entities, BlueprintId blueprintId)
    {
        var removed = entities.Where(e => IsStreamDataModel(e.CkTypeId?.ModelId)).ToList();
        entities.RemoveAll(removed.Contains);
        return removed
            .Where(e => !e.RtId.Equals(OctoObjectId.Empty))
            .Select(e => new BlueprintArchiveCandidate(e.RtId, e.RtWellKnownName, e.CkTypeId!, blueprintId,
                IsNew: true, OptedOut: false, NotImported: true))
            .ToList();
    }
}
