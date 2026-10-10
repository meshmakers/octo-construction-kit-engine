using System.Security.Cryptography;
using System.Text;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers.RangeRetention;

/// <summary>
///     CK v2 (AB#4472): computes the <c>usedSurface</c> of every declared dependency of a range-retaining compiled
///     model — the dependency's elements and members the model uses — plus a sha256 over it. Input is the
///     range-retaining output (references into dependencies are already major-qualified) and the graph the model
///     was resolved against.
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item><b>Element level</b> (every reference the compiler resolves, see
///             <see cref="CkReferenceRewriter.CollectReferences" />): base types and records, implemented interfaces,
///             interface <c>extends</c>, reused attribute definitions (type, record, role and interface assignments),
///             records and enums used as value types (attributes, method parameters and results), association roles
///             and association target types, interfaces and target attributes. A derive or implement binds the
///             element's whole public surface, so it is recorded as the element.</item>
///         <item><b>Member level</b>: attribute paths into an inherited dependency type — index fields and
///             <c>ownerAttributePath</c> — as <c>System@2/Entity-1.Name</c> (first path segment, matched
///             case-insensitively like MongoDB, at the nearest base type that declares it).</item>
///         <item>References into the model itself are never listed; internal dependency elements cannot appear,
///             the compiler rejects them (message 112). References into transitive (undeclared) dependencies have no
///             <c>dependencyRanges</c> entry and are not listed (they are floor-checked, review L14).</item>
///     </list>
/// </remarks>
public static class CkUsedSurfaceCollector
{
    /// <summary>
    ///     Fills <see cref="CkModelDependencyDto.UsedSurface" /> and <see cref="CkModelDependencyDto.UsedSurfaceHash" />
    ///     of every entry of <paramref name="output" /><c>.DependencyRanges</c>.
    /// </summary>
    public static void Apply(CkCompiledModelRoot output, CkModelGraph? modelGraph)
    {
        if (output.DependencyRanges == null)
        {
            return;
        }

        var ownName = output.ModelId.Name;
        var surfaces = output.DependencyRanges
            .GroupBy(d => d.Range.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, _ => new SortedSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);

        foreach (var (modelId, _, elementId) in CkReferenceRewriter.CollectReferences(output))
        {
            if (modelId.Name != ownName && surfaces.TryGetValue(modelId.Name, out var surface))
            {
                surface.Add($"{Qualified(modelId)}/{elementId}");
            }
        }

        if (modelGraph != null)
        {
            CollectInheritedMemberPaths(output, modelGraph, surfaces);
        }

        foreach (var dependency in output.DependencyRanges)
        {
            var list = surfaces[dependency.Range.Name].ToList();
            dependency.UsedSurface = list;
            dependency.UsedSurfaceHash = Hash(list);
        }
    }

    /// <summary>
    ///     <c>sha256:&lt;hex&gt;</c> over the ids, one per line (UTF-8, '\n', no trailing newline).
    /// </summary>
    public static string Hash(IEnumerable<string> usedSurface)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", usedSurface)));
        var hex = new StringBuilder("sha256:", 7 + bytes.Length * 2);
        foreach (var b in bytes)
        {
            hex.Append(b.ToString("x2"));
        }

        return hex.ToString();
    }

    private static string Qualified(CkModelId modelId) => $"{modelId.Name}@{modelId.Version.Major}";

    private static void CollectInheritedMemberPaths(CkCompiledModelRoot output, CkModelGraph modelGraph,
        Dictionary<string, SortedSet<string>> surfaces)
    {
        var ownName = output.ModelId.Name;
        var graphTypes = modelGraph.Types.Values
            .Where(t => t.CkTypeId.ModelId.Name == ownName)
            .ToDictionary(t => t.CkTypeId.ElementId.FullName, StringComparer.Ordinal);

        foreach (var type in output.Types ?? [])
        {
            if (!graphTypes.TryGetValue(type.TypeId.FullName, out var typeGraph))
            {
                continue;
            }

            var paths = (type.Indexes ?? []).SelectMany(i => i.Fields).SelectMany(f => f.AttributePaths).ToList();
            if (type.OwnerAttributePath != null)
            {
                paths.Add(type.OwnerAttributePath);
            }

            foreach (var path in paths)
            {
                var name = path.Split('.')[0];
                if ((type.Attributes ?? []).Any(a => string.Equals(a.AttributeName, name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                foreach (var baseId in typeGraph.BaseTypes.OrderBy(b => b.BaseTypeDepthIndex).Select(b => b.BaseCkTypeId))
                {
                    if (!modelGraph.Types.TryGetValue(baseId, out var baseGraph))
                    {
                        continue;
                    }

                    var declared = baseGraph.DefinedAttributes.FirstOrDefault(a =>
                        string.Equals(a.AttributeName, name, StringComparison.OrdinalIgnoreCase));
                    if (declared == null)
                    {
                        continue;
                    }

                    if (baseId.ModelId.Name != ownName && surfaces.TryGetValue(baseId.ModelId.Name, out var surface))
                    {
                        surface.Add($"{Qualified(baseId.ModelId)}/{baseId.ElementId.FullName}.{declared.AttributeName}");
                    }

                    break;
                }
            }
        }
    }
}
