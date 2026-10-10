using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.RangeRetention;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     Default implementation of <see cref="ICkSurfaceSatisfactionChecker" />.
/// </summary>
public class CkSurfaceSatisfactionChecker : ICkSurfaceSatisfactionChecker
{
    private readonly ICkSemVerClassifier _classifier;

    /// <summary>
    ///     Creates a new instance of the <see cref="CkSurfaceSatisfactionChecker" /> class.
    /// </summary>
    public CkSurfaceSatisfactionChecker(ICkSemVerClassifier classifier)
    {
        _classifier = classifier;
    }

    /// <inheritdoc />
    public CkDependentCheck Check(CkSurfaceCandidate candidate, CkCompiledModelRoot dependent)
    {
        var candidateId = candidate.Model.ModelId;
        var baseName = candidateId.Name;
        var candidateVersion = candidateId.Version;
        var isRange = dependent.IsRangeRetaining;
        var entry = dependent.DependencyRanges?.FirstOrDefault(d => string.Equals(d.Range.Name, baseName, StringComparison.OrdinalIgnoreCase));
        var pin = dependent.Dependencies?.FirstOrDefault(d => string.Equals(d.Name, baseName, StringComparison.OrdinalIgnoreCase));

        CkDependentCheck Result(CkDependentVerdict verdict, IReadOnlyList<string> reasons,
            CkSemVerLevel? level = null) => new()
        {
            Dependent = dependent.ModelId, IsRangeRetaining = isRange, Verdict = verdict, RequiredLevel = level,
            Reasons = reasons
        };

        if (entry == null && pin == null)
        {
            // Listed because it depends on a dependent of the candidate: nothing in it binds to the candidate directly.
            return Result(CkDependentVerdict.Compatible,
                [$"no direct dependency on {baseName}; affected only through the dependents it depends on"]);
        }

        // 1. Range / pin: is the candidate's major the one the dependent uses?
        if (entry != null)
        {
            var range = entry.Range.ModelVersionRange;
            if (!range.IsSatisfiedBy(candidateVersion))
            {
                var floor = entry.FloorVersion;
                return candidateVersion.Major == floor.Major && candidateVersion.CompareTo(floor) < 0
                    ? Result(CkDependentVerdict.Breaks,
                        [$"floor not met: {dependent.ModelId.FullName} requires {baseName} >= {floor} (range {range}), the candidate is {candidateVersion}"])
                    : Result(CkDependentVerdict.NotInRange,
                        [$"not in range: range {range} of {baseName} does not admit {candidateId.FullName}"]);
            }
        }
        else if (pin!.Version.Major != candidateVersion.Major)
        {
            return Result(CkDependentVerdict.NotInRange,
                [$"not in range: pinned to {pin.FullName}, another major than the candidate {candidateId.FullName}"]);
        }
        else if (candidateVersion.CompareTo(pin.Version) < 0)
        {
            return Result(CkDependentVerdict.NotInRange,
                [$"not in range: the candidate {candidateId.FullName} is older than the pinned {pin.FullName}"]);
        }

        // 2. Referenced surface
        var findings = new List<string>();
        var references = CkReferenceRewriter.CollectReferences(dependent)
            .Where(r => string.Equals(r.ModelId.Name, baseName, StringComparison.OrdinalIgnoreCase) && r.ModelId.Version.Major == candidateVersion.Major)
            .Distinct()
            .OrderBy(r => r.Kind, StringComparer.Ordinal).ThenBy(r => r.ElementId, StringComparer.Ordinal)
            .ToList();
        foreach (var (modelId, kind, elementId) in references)
        {
            var label = $"{dependent.ModelId.FullName} uses {modelId.FullName}/{elementId}";
            if (!candidate.HasElement(kind, elementId))
            {
                findings.Add($"{label}, which {candidateId.FullName} does not define ({kind} removed or re-identified)");
                continue;
            }

            if (candidate.Visibility.IsInternal(ToElementKind(kind), elementId) == true)
            {
                findings.Add($"{label}, which is internal in {candidateId.FullName}");
                continue;
            }

            foreach (var change in IncompatibleChanges(candidate, kind, elementId))
            {
                findings.Add($"{label}, which changes incompatibly: " +
                             $"{CkModelChangeFormatter.Format(change.Change)} ({change.Reason})");
            }
        }

        foreach (var member in entry?.UsedSurface?.Where(IsMemberEntry) ?? [])
        {
            AddMemberFindings(candidate, dependent, member, findings);
        }

        // 3. Name collisions with derived types (H6 / N2)
        AddCollisionFindings(candidate.WithDependent(dependent), dependent, findings);

        if (findings.Count > 0)
        {
            return Result(CkDependentVerdict.Breaks, findings);
        }

        var surface = references.Count == 0
            ? "no references into the candidate"
            : $"{references.Count} referenced element(s) exist, are public and unchanged incompatibly";
        if (isRange || pin == null || candidateVersion.CompareTo(pin.Version) == 0)
        {
            return Result(CkDependentVerdict.Compatible,
                [entry != null ? $"range {entry.Range.ModelVersionRange} admits {candidateVersion}; {surface}" : surface]);
        }

        var level = RepinLevel(dependent, baseName, pin.Version, candidateVersion);
        return Result(CkDependentVerdict.NeedsRepin,
            [$"pinned to {pin.FullName}; {surface}; recompile and republish against {candidateId.FullName} " +
             $"({CkModelChangeFormatter.GetLevelLabel(level)} bump)"], level);
    }

    private CkSemVerLevel RepinLevel(CkCompiledModelRoot dependent, string baseName, CkVersion pinned,
        CkVersion candidate)
    {
        var change = new CkModelChange
        {
            ChangeKind = CkModelChangeKind.Modified, ElementKind = CkModelElementKind.Dependency,
            ElementId = baseName, Property = "version", OldValue = pinned.ToString(), NewValue = candidate.ToString()
        };
        return _classifier.GetRequiredLevel(_classifier.Classify([change], dependent, dependent));
    }

    private static CkModelElementKind ToElementKind(string kind) => kind switch
    {
        "type" => CkModelElementKind.Type,
        "attribute" => CkModelElementKind.Attribute,
        "record" => CkModelElementKind.Record,
        "enum" => CkModelElementKind.Enum,
        "association role" => CkModelElementKind.AssociationRole,
        "interface" => CkModelElementKind.Interface,
        _ => CkModelElementKind.Model
    };

    /// <summary>Element kinds of the diff that belong to a referenced element (the element itself and its members).</summary>
    private static bool BelongsTo(string kind, CkModelElementKind changeKind) => kind switch
    {
        "type" => changeKind is CkModelElementKind.Type or CkModelElementKind.TypeAttribute
            or CkModelElementKind.TypeAssociation or CkModelElementKind.TypeIndex or CkModelElementKind.TypeInterface
            or CkModelElementKind.TypeMethod or CkModelElementKind.MethodParameter or CkModelElementKind.MethodError,
        "record" => changeKind is CkModelElementKind.Record or CkModelElementKind.RecordAttribute,
        "enum" => changeKind is CkModelElementKind.Enum or CkModelElementKind.EnumValue,
        "attribute" => changeKind is CkModelElementKind.Attribute,
        "association role" => changeKind is CkModelElementKind.AssociationRole
            or CkModelElementKind.AssociationRoleAttribute,
        "interface" => changeKind is CkModelElementKind.Interface or CkModelElementKind.InterfaceAttribute
            or CkModelElementKind.InterfaceExtends or CkModelElementKind.InterfaceAssociation
            or CkModelElementKind.InterfaceMethod or CkModelElementKind.MethodParameter
            or CkModelElementKind.MethodError,
        _ => false
    };

    private static IEnumerable<CkClassifiedModelChange> IncompatibleChanges(CkSurfaceCandidate candidate, string kind,
        string elementId) =>
        candidate.ClassifiedChanges.Where(c =>
            c.Level == CkSemVerLevel.Major && BelongsTo(kind, c.Change.ElementKind) &&
            (c.Change.ElementId == elementId ||
             c.Change.ElementId.StartsWith(elementId + "/", StringComparison.Ordinal) ||
             c.Change.ElementId.StartsWith(elementId + ".", StringComparison.Ordinal)));

    /// <summary><c>Name@2/Entity-1.Name</c>: a member of an element (the element itself has no '.').</summary>
    private static bool IsMemberEntry(string usedSurface)
    {
        var slash = usedSurface.IndexOf('/');
        return slash >= 0 && usedSurface.IndexOf('.', slash) > slash;
    }

    private static void AddMemberFindings(CkSurfaceCandidate candidate, CkCompiledModelRoot dependent, string entry,
        List<string> findings)
    {
        var slash = entry.IndexOf('/');
        var dot = entry.IndexOf('.', slash);
        var typeId = entry.Substring(slash + 1, dot - slash - 1);
        var member = entry.Substring(dot + 1);
        var label = $"{dependent.ModelId.FullName} uses {entry}";

        var type = candidate.Model.Types?.FirstOrDefault(t => t.TypeId.FullName == typeId);
        // Walk up the candidate's own hierarchy; a base type outside the candidate cannot be inspected here.
        while (type != null)
        {
            if ((type.Attributes ?? []).Any(a => string.Equals(a.AttributeName, member, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var parent = type.DerivedFromCkTypeId;
            if (parent != null && !parent.IsEmpty && parent.ModelId != null! &&
                parent.ModelId.Name != candidate.Model.ModelId.Name)
            {
                return; // the base type lives in another model and cannot be inspected here
            }

            if (parent == null || parent.IsEmpty)
            {
                break; // root of the hierarchy: the member is not declared anywhere
            }

            type = candidate.Model.Types?.FirstOrDefault(t => t.TypeId.FullName == parent.ElementId.FullName);
        }

        if (candidate.HasElement("type", typeId))
        {
            findings.Add($"{label}: the attribute '{member}' is no longer declared by {candidate.Model.ModelId.FullName}/{typeId} or its bases");
        }
    }

    /// <summary>
    ///     Rows H6 / N2: an attribute or method the candidate newly declares on a type (also a method it already
    ///     inherited from an implemented interface) collides with a member of a type of the dependent that derives from
    ///     it — messages 13 / 100 at the dependent's next compile.
    /// </summary>
    private static void AddCollisionFindings(CkSurfaceCandidate candidate, CkCompiledModelRoot dependent,
        List<string> findings)
    {
        if (candidate.Baseline?.Types == null)
        {
            return;
        }

        foreach (var type in candidate.Model.Types ?? [])
        {
            var before = candidate.Baseline.Types.FirstOrDefault(t => t.TypeId.FullName == type.TypeId.FullName);
            if (before == null)
            {
                continue;
            }

            var addedAttributes = (type.Attributes ?? []).Select(a => a.AttributeName)
                .Where(n => !(before.Attributes ?? []).Any(a => string.Equals(a.AttributeName, n, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            var addedMethods = (type.Methods ?? []).Select(m => m.MethodId)
                .Where(id => !(before.Methods ?? []).Any(m => m.MethodId == id))
                .ToList();
            if (addedAttributes.Count == 0 && addedMethods.Count == 0)
            {
                continue;
            }

            var baseLabel = $"{candidate.Model.ModelId.FullName}/{type.TypeId.FullName}";
            foreach (var (model, derived) in candidate.DescendantsOf(type.TypeId.FullName)
                         .Where(d => d.Model.ModelId.Name == dependent.ModelId.Name))
            {
                var derivedLabel = $"{model.ModelId.FullName}/{derived.TypeId.FullName}";
                foreach (var name in addedAttributes.Where(n =>
                             (derived.Attributes ?? []).Any(a => string.Equals(a.AttributeName, n, StringComparison.OrdinalIgnoreCase))))
                {
                    findings.Add($"{baseLabel} adds the attribute '{name}', which the derived type {derivedLabel} " +
                                 "already declares (name collision, error 13)");
                }

                foreach (var id in addedMethods.Where(id => (derived.Methods ?? []).Any(m => m.MethodId == id)))
                {
                    var inherited = IsInheritedFromInterface(candidate.Model, type, id)
                        ? " (row N2: the base now redeclares a method it inherited from an implemented interface)"
                        : "";
                    findings.Add($"{baseLabel} adds the method '{id}'{inherited}, which the derived type " +
                                 $"{derivedLabel} already declares (duplicate method id, error 100)");
                }
            }
        }
    }

    private static bool IsInheritedFromInterface(CkCompiledModelRoot model, CkCompiledTypeDto type, string methodId) =>
        (type.Implements ?? []).Any(implemented =>
            implemented.ModelId?.Name == model.ModelId.Name &&
            (model.Interfaces ?? []).Any(i => i.InterfaceId.FullName == implemented.ElementId.FullName &&
                                              (i.Methods ?? []).Any(m => m.MethodId == methodId)));
}
