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
                _methods[$"{ckInterface.InterfaceId.FullName}/{method.MethodId}"] =
                    ownerInternal || CkModifiers.ResolveVisibility(method.Visibility) == CkVisibilityDto.Internal;
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
