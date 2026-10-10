using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     Effective visibility of every element of one compiled model, keyed like the element ids of
///     <see cref="CkModelDiffService" /> (AB#6266). Answers whether a change's element is internal, itself or through
///     its owner (a type attribute through its type, a method parameter through its method and that method's owner).
/// </summary>
internal sealed class CkVisibilityIndex
{
    private readonly Dictionary<CkModelElementKind, Dictionary<string, bool>> _elements = new();
    private readonly Dictionary<string, bool> _methods = new(StringComparer.Ordinal);

    /// <summary>
    ///     AB#6334 / N1: associations of internal types that target a public type (element ids as in the diff). They
    ///     create an inbound navigation on the public target (GraphQL), so they are public surface.
    /// </summary>
    private readonly HashSet<string> _exposedAssociations = new(StringComparer.Ordinal);

    /// <summary>Internal types that own at least one exposed association (see <see cref="_exposedAssociations" />).</summary>
    private readonly HashSet<string> _typesWithExposedAssociations = new(StringComparer.Ordinal);

    public CkVisibilityIndex(CkCompiledModelRoot model)
    {
        void Add<T>(CkModelElementKind kind, IEnumerable<T>? elements, Func<T, string> id, Func<T, CkVisibilityDto?> visibility)
        {
            var map = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var element in elements ?? [])
            {
                map[id(element)] = CkModifiers.ResolveVisibility(visibility(element)) == CkVisibilityDto.Internal;
            }

            _elements[kind] = map;
        }

        Add(CkModelElementKind.Type, model.Types, t => t.TypeId.FullName, t => t.Visibility);
        Add(CkModelElementKind.Record, model.Records, r => r.RecordId.FullName, r => r.Visibility);
        Add(CkModelElementKind.Enum, model.Enums, e => e.EnumId.FullName, e => e.Visibility);
        Add(CkModelElementKind.Attribute, model.Attributes, a => a.AttributeId.FullName, a => a.Visibility);
        Add(CkModelElementKind.AssociationRole, model.AssociationRoles, r => r.AssociationRoleId.FullName, r => r.Visibility);
        Add(CkModelElementKind.Interface, model.Interfaces, i => i.InterfaceId.FullName, i => i.Visibility);

        foreach (var type in model.Types ?? [])
        {
            var ownerInternal = _elements[CkModelElementKind.Type][type.TypeId.FullName];
            foreach (var method in type.Methods ?? [])
            {
                _methods[$"{type.TypeId.FullName}/{method.MethodId}"] =
                    ownerInternal || CkModifiers.ResolveVisibility(method.Visibility) == CkVisibilityDto.Internal;
            }
        }

        foreach (var ckInterface in model.Interfaces ?? [])
        {
            var ownerInternal = _elements[CkModelElementKind.Interface][ckInterface.InterfaceId.FullName];
            foreach (var method in ckInterface.Methods ?? [])
            {
                // AB#6335 defence in depth: a method of a public interface is part of the interface contract even
                // when it is (inconsistently) declared internal.
                _methods[$"{ckInterface.InterfaceId.FullName}/{method.MethodId}"] = ownerInternal;
            }
        }

        ExposeInternalElementsReachableFromPublicOnes(model);
    }

    /// <summary>
    ///     AB#6334 defence in depth: the compiler rejects a public element that references an internal element of its
    ///     own model (129). A model compiled by an older ckc may still contain such references; an internal element a
    ///     public element reaches is part of the compatibility surface, so it is treated as public here (the cap of
    ///     rows N1–N5 does not apply). Transitive: an exposed element exposes what it references. The reference walk
    ///     mirrors <c>CkVisibilityValidator</c>.
    /// </summary>
    private void ExposeInternalElementsReachableFromPublicOnes(CkCompiledModelRoot model)
    {
        var modelName = model.ModelId.Name;
        var changed = true;

        void Expose<T>(CkModelElementKind kind, CkId<T>? id) where T : IComparable<T>, ICkElementId
        {
            if (id != null && string.Equals(id.ModelId.Name, modelName, StringComparison.Ordinal) &&
                _elements[kind].TryGetValue(id.ElementId.FullName, out var isInternal) && isInternal)
            {
                _elements[kind][id.ElementId.FullName] = false;
                changed = true;
            }
        }

        bool IsPublic(CkModelElementKind kind, string id) =>
            _elements[kind].TryGetValue(id, out var isInternal) && !isInternal;

        void ExposeMethods(string ownerKey, IEnumerable<CkMethodDto>? methods)
        {
            foreach (var method in methods ?? [])
            {
                if (_methods.TryGetValue($"{ownerKey}/{method.MethodId}", out var isInternal) && isInternal)
                {
                    continue;
                }

                foreach (var parameter in method.Parameters ?? [])
                {
                    Expose(CkModelElementKind.Record, parameter.ValueCkRecordId);
                    Expose(CkModelElementKind.Enum, parameter.ValueCkEnumId);
                }

                Expose(CkModelElementKind.Record, method.Result?.ValueCkRecordId);
                Expose(CkModelElementKind.Enum, method.Result?.ValueCkEnumId);
            }
        }

        void ExposeAssignments(IEnumerable<CkTypeAttributeDto>? assignments)
        {
            foreach (var assignment in assignments ?? [])
            {
                Expose(CkModelElementKind.Attribute, assignment.CkAttributeId);
            }
        }

        while (changed)
        {
            changed = false;

            // N1 (platform-owner decision 2026-10-10): an association of an INTERNAL type that targets a PUBLIC type
            // (of this model or a dependency) adds an inbound navigation to the public target in GraphQL; derived
            // types in other models inherit it. The association and its role (inbound name, multiplicities) are
            // therefore public surface — removing the association or renaming the role is not capped.
            foreach (var type in (model.Types ?? []).Where(t => !IsPublic(CkModelElementKind.Type, t.TypeId.FullName)))
            {
                foreach (var association in type.Associations ?? [])
                {
                    var target = association.TargetCkTypeId;
                    var targetIsPublic = !string.Equals(target.ModelId.Name, modelName, StringComparison.Ordinal) ||
                                         IsPublic(CkModelElementKind.Type, target.ElementId.FullName);
                    if (!targetIsPublic)
                    {
                        continue;
                    }

                    _typesWithExposedAssociations.Add(type.TypeId.FullName);
                    _exposedAssociations.Add(AssociationKey(type.TypeId.FullName, association.CkRoleId.ElementId.FullName,
                        association.TargetCkTypeId.ElementId.FullName));
                    Expose(CkModelElementKind.AssociationRole, association.CkRoleId);
                }
            }

            foreach (var type in (model.Types ?? []).Where(t => IsPublic(CkModelElementKind.Type, t.TypeId.FullName)))
            {
                Expose(CkModelElementKind.Type, type.DerivedFromCkTypeId);
                ExposeAssignments(type.Attributes);
                foreach (var implemented in type.Implements ?? [])
                {
                    Expose(CkModelElementKind.Interface, implemented);
                }

                foreach (var association in type.Associations ?? [])
                {
                    Expose(CkModelElementKind.AssociationRole, association.CkRoleId);
                    Expose(CkModelElementKind.Type, association.TargetCkTypeId);
                    Expose(CkModelElementKind.Interface, association.TargetCkInterfaceId);
                    foreach (var targetAttribute in association.TargetCkAttributeIds ?? [])
                    {
                        Expose(CkModelElementKind.Attribute, targetAttribute);
                    }
                }

                ExposeMethods(type.TypeId.FullName, type.Methods);
            }

            foreach (var record in (model.Records ?? []).Where(r =>
                         IsPublic(CkModelElementKind.Record, r.RecordId.FullName)))
            {
                Expose(CkModelElementKind.Record, record.DerivedFromCkRecordId);
                ExposeAssignments(record.Attributes);
            }

            foreach (var role in (model.AssociationRoles ?? []).Where(r =>
                         IsPublic(CkModelElementKind.AssociationRole, r.AssociationRoleId.FullName)))
            {
                ExposeAssignments(role.Attributes);
            }

            foreach (var attribute in (model.Attributes ?? []).Where(a =>
                         IsPublic(CkModelElementKind.Attribute, a.AttributeId.FullName)))
            {
                Expose(CkModelElementKind.Record, attribute.ValueCkRecordId);
                Expose(CkModelElementKind.Enum, attribute.ValueCkEnumId);
            }

            foreach (var ckInterface in (model.Interfaces ?? []).Where(i =>
                         IsPublic(CkModelElementKind.Interface, i.InterfaceId.FullName)))
            {
                foreach (var member in ckInterface.Attributes)
                {
                    Expose(CkModelElementKind.Attribute, member.CkAttributeId);
                }

                foreach (var extended in ckInterface.Extends ?? [])
                {
                    Expose(CkModelElementKind.Interface, extended);
                }

                foreach (var association in ckInterface.Associations ?? [])
                {
                    Expose(CkModelElementKind.AssociationRole, association.CkRoleId);
                    Expose(CkModelElementKind.Type, association.TargetCkTypeId);
                    Expose(CkModelElementKind.Interface, association.TargetCkInterfaceId);
                }

                ExposeMethods(ckInterface.InterfaceId.FullName, ckInterface.Methods);
            }

            // An exposed owner exposes its methods (the method keys were computed from the declared visibility).
            foreach (var type in model.Types ?? [])
            {
                if (!IsPublic(CkModelElementKind.Type, type.TypeId.FullName))
                {
                    continue;
                }

                foreach (var method in type.Methods ?? [])
                {
                    var key = $"{type.TypeId.FullName}/{method.MethodId}";
                    var declaredInternal = CkModifiers.ResolveVisibility(method.Visibility) == CkVisibilityDto.Internal;
                    if (_methods[key] && !declaredInternal)
                    {
                        _methods[key] = false;
                        changed = true;
                    }
                }
            }

            foreach (var ckInterface in model.Interfaces ?? [])
            {
                if (!IsPublic(CkModelElementKind.Interface, ckInterface.InterfaceId.FullName))
                {
                    continue;
                }

                foreach (var method in ckInterface.Methods ?? [])
                {
                    var key = $"{ckInterface.InterfaceId.FullName}/{method.MethodId}";
                    if (_methods[key])
                    {
                        _methods[key] = false;
                        changed = true;
                    }
                }
            }
        }
    }

    /// <summary>
    ///     True when the element (or its owner) is internal in this model, false when it is public, null when neither
    ///     the element nor its owner exists here or the kind has no visibility (model, dependencies).
    /// </summary>
    public bool? IsInternal(CkModelElementKind kind, string elementId)
    {
        switch (kind)
        {
            case CkModelElementKind.Type or CkModelElementKind.Record or CkModelElementKind.Enum
                or CkModelElementKind.Attribute or CkModelElementKind.AssociationRole or CkModelElementKind.Interface:
                return Lookup(kind, elementId);
            case CkModelElementKind.TypeAssociation when IsExposedAssociation(elementId):
                return false;
            case CkModelElementKind.TypeAttribute or CkModelElementKind.TypeAssociation or CkModelElementKind.TypeIndex
                or CkModelElementKind.TypeInterface:
                return Lookup(CkModelElementKind.Type, Segments(elementId, 1));
            case CkModelElementKind.RecordAttribute:
                return Lookup(CkModelElementKind.Record, Segments(elementId, 1));
            case CkModelElementKind.AssociationRoleAttribute:
                return Lookup(CkModelElementKind.AssociationRole, Segments(elementId, 1));
            case CkModelElementKind.EnumValue:
                return Lookup(CkModelElementKind.Enum, Segments(elementId, 1));
            case CkModelElementKind.InterfaceAttribute or CkModelElementKind.InterfaceExtends
                or CkModelElementKind.InterfaceAssociation:
                return Lookup(CkModelElementKind.Interface, Segments(elementId, 1));
            case CkModelElementKind.TypeMethod or CkModelElementKind.InterfaceMethod:
                return LookupMethod(elementId);
            case CkModelElementKind.MethodParameter or CkModelElementKind.MethodError:
                return LookupMethod(Segments(elementId, 2));
            default:
                return null;
        }
    }

    /// <summary>
    ///     True for an internal type that owns an association to a public type (N1). Removing or renaming such a type
    ///     removes the inbound navigation from the public target (gate re-run P3-1), so the removal is not capped.
    /// </summary>
    public bool OwnsExposedAssociation(string typeId) => _typesWithExposedAssociations.Contains(typeId);

    private static string AssociationKey(string typeId, string roleElementId, string targetElementId) =>
        $"{typeId}|{roleElementId}|{targetElementId}";

    /// <summary>
    ///     Type association element ids are <c>&lt;type&gt;/&lt;role reference&gt; -&gt; &lt;target reference&gt;</c>;
    ///     references end with <c>/&lt;element&gt;</c> whatever their model rendering, so the key uses element names.
    /// </summary>
    private bool IsExposedAssociation(string elementId)
    {
        if (_exposedAssociations.Count == 0)
        {
            return false;
        }

        var ownerEnd = elementId.IndexOf('/');
        var arrow = elementId.IndexOf(" -> ", StringComparison.Ordinal);
        if (ownerEnd < 0 || arrow < ownerEnd)
        {
            return false;
        }

        var role = elementId.Substring(ownerEnd + 1, arrow - ownerEnd - 1);
        var target = elementId.Substring(arrow + 4);
        return _exposedAssociations.Contains(AssociationKey(elementId.Substring(0, ownerEnd),
            role.Substring(role.LastIndexOf('/') + 1), target.Substring(target.LastIndexOf('/') + 1)));
    }

    private bool? Lookup(CkModelElementKind kind, string? id) =>
        id != null && _elements.TryGetValue(kind, out var map) && map.TryGetValue(id, out var isInternal)
            ? isInternal
            : null;

    private bool? LookupMethod(string? id) =>
        id != null && _methods.TryGetValue(id, out var isInternal) ? isInternal : null;

    /// <summary>
    ///     The first <paramref name="count" /> '/'-separated segments of an element id (element names contain no '/').
    /// </summary>
    private static string? Segments(string elementId, int count)
    {
        var index = -1;
        for (var i = 0; i < count; i++)
        {
            index = elementId.IndexOf('/', index + 1);
            if (index < 0)
            {
                return null;
            }
        }

        return elementId.Substring(0, index);
    }
}
