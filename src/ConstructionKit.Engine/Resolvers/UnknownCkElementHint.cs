using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
///     AB#6987: explains why a referenced type or record id is unknown. Only the hint text of the existing
///     messages (<c>OCTO-CK018</c>, <c>OCTO-CK019</c>, <c>OCTO-CK034</c>) and of the matching
///     <see cref="ModelValidationException" />s depends on it; codes and levels are unchanged.
/// </summary>
/// <remarks>
///     Three situations are told apart: (a) the model of the id is not part of the graph - a dependency or alias is
///     missing (the former, only hint); (b) the model is resolved but holds no element of that name; (c) an element
///     of that name exists in another model of the graph.
/// </remarks>
internal static class UnknownCkElementHint
{
    internal const string DependencyMissing =
        "This may happen because a dependency to another construction kit model is missing.";

    private const int MaxSuggestions = 3;

    internal static string ForType(CkModelGraph graph, CkId<CkTypeId> ckTypeId)
    {
        return Build(graph, "type", ckTypeId.ModelId, ckTypeId.ElementId.Name,
            ckTypeId.ElementId.Version, graph.Types.Keys.Select(k => (k.ModelId, k.ElementId.Name, k.ElementId.Version)));
    }

    internal static string ForRecord(CkModelGraph graph, CkId<CkRecordId> ckRecordId)
    {
        return Build(graph, "record", ckRecordId.ModelId, ckRecordId.ElementId.Name,
            ckRecordId.ElementId.Version, graph.Records.Keys.Select(k => (k.ModelId, k.ElementId.Name, k.ElementId.Version)));
    }

    private static string Build(CkModelGraph graph, string kind, CkModelId modelId, string name,
        uint version, IEnumerable<(CkModelId ModelId, string Name, uint Version)> elements)
    {
        var all = elements.ToList();
        var modelResolved = graph.Models.ContainsKey(modelId) || all.Any(e => e.ModelId.Equals(modelId));
        if (!modelResolved)
        {
            return DependencyMissing;
        }

        var modelFullName = modelId.FullName;
        var otherVersions = all
            .Where(e => e.ModelId.Equals(modelId) && string.Equals(e.Name, name, StringComparison.Ordinal) &&
                        e.Version != version)
            .Select(e => $"{e.Name}-{e.Version}")
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();
        if (otherVersions.Count > 0)
        {
            return $"The {kind} '{name}-{version}' does not exist in model {modelFullName}, but the model contains " +
                   $"{string.Join(", ", otherVersions.Select(v => $"'{v}'"))}. Check the version of the reference.";
        }

        var inOtherModels = all
            .Where(e => !e.ModelId.Equals(modelId) && string.Equals(e.Name, name, StringComparison.Ordinal))
            .Select(e => e.ModelId.FullName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToList();
        if (inOtherModels.Count > 0)
        {
            return $"The {kind} '{name}' does not exist in model {modelFullName}, but a {kind} of that name exists in " +
                   $"{string.Join(", ", inOtherModels)}. Check the model prefix of the reference.";
        }

        var suggestions = Suggest(name, all.Where(e => e.ModelId.Equals(modelId)).Select(e => e.Name));
        var hint = $"The {kind} '{name}' does not exist in model {modelFullName}.";
        if (suggestions.Count > 0)
        {
            hint += $" Did you mean {string.Join(", ", suggestions.Select(s => $"'{s}'"))}?";
        }

        return hint;
    }

    private static List<string> Suggest(string name, IEnumerable<string> candidates)
    {
        var maxDistance = Math.Max(2, name.Length / 3);
        return candidates
            .Distinct(StringComparer.Ordinal)
            .Select(c => (Name: c, Distance: Distance(name, c)))
            .Where(c => c.Distance <= maxDistance ||
                        c.Name.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                        name.Contains(c.Name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Distance)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .Take(MaxSuggestions)
            .Select(c => c.Name)
            .ToList();
    }

    // Case-insensitive Levenshtein distance.
    private static int Distance(string a, string b)
    {
        a = a.ToLowerInvariant();
        b = b.ToLowerInvariant();
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
