using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers.RangeRetention;

/// <summary>
///     Rewrites the model part of every element reference of a CK model (CK v2 range retention, AB#5664 /
///     AB#5665). These are all places where a model refers to elements of another model: attribute
///     record/enum value types, type and record base types, type/record/association-role attribute
///     assignments, association roles, association target types and target attributes.
/// </summary>
internal static class CkReferenceRewriter
{
    /// <summary>
    ///     Applies <paramref name="map" /> to the model id of every element reference in place.
    ///     <paramref name="map" /> returns the replacement model id or <c>null</c> to keep the reference.
    /// </summary>
    /// <returns>The number of rewritten references.</returns>
    public static int Rewrite(CkModelRootBase model, Func<CkModelId, CkModelId?> map)
    {
        var count = 0;

        CkId<T>? Map<T>(CkId<T>? id) where T : IComparable<T>, ICkElementId
        {
            if (id == null || id.ModelId == null! || id.IsEmpty)
            {
                return id;
            }

            var replacement = map(id.ModelId);
            if (replacement == null || replacement.Equals(id.ModelId))
            {
                return id;
            }

            count++;
            return new CkId<T>(replacement, id.ElementId);
        }

        void MapAttributes(List<CkTypeAttributeDto>? attributes)
        {
            if (attributes == null)
            {
                return;
            }

            foreach (var attribute in attributes)
            {
                attribute.CkAttributeId = Map(attribute.CkAttributeId)!;
            }
        }

        foreach (var attribute in model.Attributes ?? [])
        {
            attribute.ValueCkRecordId = Map(attribute.ValueCkRecordId);
            attribute.ValueCkEnumId = Map(attribute.ValueCkEnumId);
        }

        foreach (var role in model.AssociationRoles ?? [])
        {
            MapAttributes(role.Attributes);
        }

        foreach (var record in model.Records ?? [])
        {
            record.DerivedFromCkRecordId = Map(record.DerivedFromCkRecordId);
            MapAttributes(record.Attributes);
        }

        foreach (var type in model.Types ?? [])
        {
            type.DerivedFromCkTypeId = Map(type.DerivedFromCkTypeId);
            MapAttributes(type.Attributes);
            foreach (var association in type.Associations ?? [])
            {
                association.CkRoleId = Map(association.CkRoleId)!;
                association.TargetCkTypeId = Map(association.TargetCkTypeId)!;
                if (association.TargetCkAttributeIds != null)
                {
                    association.TargetCkAttributeIds = association.TargetCkAttributeIds.Select(a => Map(a)!).ToList();
                }
            }
        }

        return count;
    }

    /// <summary>
    ///     Enumerates the model ids of every element reference.
    /// </summary>
    public static IReadOnlyList<CkModelId> CollectReferencedModels(CkModelRootBase model)
    {
        var result = new List<CkModelId>();
        Rewrite(model, id =>
        {
            result.Add(id);
            return null;
        });
        return result;
    }

    /// <summary>
    ///     Enumerates every element reference as (model id, element kind, element id name) for the floor check.
    /// </summary>
    public static IReadOnlyList<(CkModelId ModelId, string Kind, string ElementId)> CollectReferences(
        CkModelRootBase model)
    {
        var result = new List<(CkModelId, string, string)>();

        void Add<T>(string kind, CkId<T>? id) where T : IComparable<T>, ICkElementId
        {
            if (id != null && id.ModelId != null! && !id.IsEmpty)
            {
                result.Add((id.ModelId, kind, id.ElementId.ToString()!));
            }
        }

        void AddAttributes(List<CkTypeAttributeDto>? attributes)
        {
            foreach (var attribute in attributes ?? [])
            {
                Add("attribute", attribute.CkAttributeId);
            }
        }

        foreach (var attribute in model.Attributes ?? [])
        {
            Add("record", attribute.ValueCkRecordId);
            Add("enum", attribute.ValueCkEnumId);
        }

        foreach (var role in model.AssociationRoles ?? [])
        {
            AddAttributes(role.Attributes);
        }

        foreach (var record in model.Records ?? [])
        {
            Add("record", record.DerivedFromCkRecordId);
            AddAttributes(record.Attributes);
        }

        foreach (var type in model.Types ?? [])
        {
            Add("type", type.DerivedFromCkTypeId);
            AddAttributes(type.Attributes);
            foreach (var association in type.Associations ?? [])
            {
                Add("association role", association.CkRoleId);
                Add("type", association.TargetCkTypeId);
                foreach (var target in association.TargetCkAttributeIds ?? [])
                {
                    Add("attribute", target);
                }
            }
        }

        return result;
    }

    /// <summary>
    ///     Binds major-qualified references (<c>System@2/Entity-1</c>) to the concrete model version that is
    ///     part of <paramref name="resolvedModels" /> with the same name and major (AB#5665). References whose
    ///     model is not resolved, or resolved in another major, stay unbound and fail reference resolution.
    /// </summary>
    public static int BindMajorQualified(CkModelRootBase model, IEnumerable<CkModelId> resolvedModels)
    {
        var byNameAndMajor = new Dictionary<(string, int), CkModelId>();
        foreach (var resolved in resolvedModels.Where(m => !m.IsMajorQualified))
        {
            var key = (resolved.Name, resolved.Version.Major);
            if (!byNameAndMajor.TryGetValue(key, out var existing) || existing.Version.CompareTo(resolved.Version) < 0)
            {
                byNameAndMajor[key] = resolved;
            }
        }

        return Rewrite(model, id => id.IsMajorQualified &&
                                    byNameAndMajor.TryGetValue((id.Name, id.Version.Major), out var bound)
            ? bound
            : null);
    }

    /// <summary>True when the model holds at least one major-qualified reference.</summary>
    public static bool HasMajorQualifiedReferences(CkModelRootBase model)
    {
        return CollectReferencedModels(model).Any(m => m.IsMajorQualified);
    }
}
