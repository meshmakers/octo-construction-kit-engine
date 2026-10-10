using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     A candidate version of a base model prepared for the surface satisfaction check (AB#5437): the compiled model,
///     the baseline it is compared with and the classified changes. Immutable; build once, check many dependents.
/// </summary>
public sealed class CkSurfaceCandidate
{
    private readonly IReadOnlyCollection<CkCompiledModelRoot> _scope;
    private readonly Dictionary<string, HashSet<string>> _elementIds;
    private readonly List<(CkCompiledModelRoot Model, CkCompiledTypeDto Type)> _scopeTypes = [];
    private readonly Dictionary<(string Model, string Type), List<(CkCompiledModelRoot Model, CkCompiledTypeDto Type)>> _children = new();

    private CkSurfaceCandidate(CkCompiledModelRoot model, CkCompiledModelRoot? baseline,
        IReadOnlyList<CkClassifiedModelChange> classifiedChanges, IReadOnlyCollection<CkCompiledModelRoot> scope)
    {
        _scope = scope;
        Model = model;
        Baseline = baseline;
        ClassifiedChanges = classifiedChanges;
        Visibility = new CkVisibilityIndex(model);
        _elementIds = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["type"] = Ids(model.Types?.Select(t => t.TypeId.FullName)),
            ["attribute"] = Ids(model.Attributes?.Select(a => a.AttributeId.FullName)),
            ["record"] = Ids(model.Records?.Select(r => r.RecordId.FullName)),
            ["enum"] = Ids(model.Enums?.Select(e => e.EnumId.FullName)),
            ["association role"] = Ids(model.AssociationRoles?.Select(r => r.AssociationRoleId.FullName)),
            ["interface"] = Ids(model.Interfaces?.Select(i => i.InterfaceId.FullName))
        };

        foreach (var dependent in scope)
        {
            foreach (var type in dependent.Types ?? [])
            {
                _scopeTypes.Add((dependent, type));
                if (type.DerivedFromCkTypeId is { IsEmpty: false } parent && parent.ModelId != null!)
                {
                    var key = (parent.ModelId.Name, parent.ElementId.FullName);
                    if (!_children.TryGetValue(key, out var list))
                    {
                        _children[key] = list = [];
                    }

                    list.Add((dependent, type));
                }
            }
        }
    }

    /// <summary>The compiled candidate.</summary>
    public CkCompiledModelRoot Model { get; }

    /// <summary>The baseline the candidate is compared with; null for a first publication.</summary>
    public CkCompiledModelRoot? Baseline { get; }

    /// <summary>The classified changes baseline -> candidate (empty without a baseline).</summary>
    public IReadOnlyList<CkClassifiedModelChange> ClassifiedChanges { get; }

    internal CkVisibilityIndex Visibility { get; }

    /// <summary>
    ///     Creates a candidate.
    /// </summary>
    /// <param name="model">The compiled candidate.</param>
    /// <param name="baseline">The baseline, null for a first publication.</param>
    /// <param name="classifiedChanges">The classified changes baseline -> candidate.</param>
    /// <param name="knownDependents">
    ///     All dependents the check will see: their types are searched for descendants of the candidate's types (name
    ///     collisions, rows H6 / N2).
    /// </param>
    public static CkSurfaceCandidate Create(CkCompiledModelRoot model, CkCompiledModelRoot? baseline,
        IReadOnlyList<CkClassifiedModelChange> classifiedChanges,
        IReadOnlyCollection<CkCompiledModelRoot>? knownDependents = null) =>
        new(model, baseline, classifiedChanges, knownDependents ?? []);

    /// <summary>
    ///     The same candidate with <paramref name="dependent" /> added to the known dependents (when it is not yet one),
    ///     so its types are searched for descendants of the candidate's types.
    /// </summary>
    internal CkSurfaceCandidate WithDependent(CkCompiledModelRoot dependent) =>
        _scope.Any(d => ReferenceEquals(d, dependent))
            ? this
            : new CkSurfaceCandidate(Model, Baseline, ClassifiedChanges, [.. _scope, dependent]);

    /// <summary>True when the candidate defines the element (<paramref name="kind" /> as in <c>CkReferenceRewriter.CollectReferences</c>).</summary>
    internal bool HasElement(string kind, string elementId) =>
        _elementIds.TryGetValue(kind, out var ids) && ids.Contains(elementId);

    /// <summary>All types of the known dependents that derive, directly or through other dependents' types, from the given candidate type.</summary>
    internal IReadOnlyList<(CkCompiledModelRoot Model, CkCompiledTypeDto Type)> DescendantsOf(string typeId)
    {
        var result = new List<(CkCompiledModelRoot, CkCompiledTypeDto)>();
        var queue = new Queue<(string Model, string Type)>();
        queue.Enqueue((Model.ModelId.Name, typeId));
        var seen = new HashSet<(string, string)> { (Model.ModelId.Name, typeId) };
        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            if (!_children.TryGetValue(parent, out var children))
            {
                continue;
            }

            foreach (var (childModel, childType) in children)
            {
                var key = (childModel.ModelId.Name, childType.TypeId.FullName);
                if (seen.Add(key))
                {
                    result.Add((childModel, childType));
                    queue.Enqueue(key);
                }
            }
        }

        return result;
    }

    private static HashSet<string> Ids(IEnumerable<string>? ids) => new(ids ?? [], StringComparer.Ordinal);
}
