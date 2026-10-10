using System.Reflection;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Resolvers;

/// <summary>
///     AB#6334 guard: every CK element reference (<c>CkId&lt;…&gt;</c> or a list of them) of the CK DTOs is either walked by
///     the visibility rules (<c>CkVisibilityValidator</c>: 112 across models, 129 inside a model; and its mirror
///     <c>CkVisibilityIndex.ExposeInternalElementsReachableFromPublicOnes</c> in the classifier) or listed here with a
///     reason why visibility does not apply. A new reference property fails this test until it is added to the walk
///     (both places) and to <see cref="Walked" />, or to <see cref="NotWalked" /> with a reason.
/// </summary>
public class CkVisibilityReferenceCoverageTests
{
    /// <summary>References the visibility walk checks.</summary>
    private static readonly HashSet<string> Walked =
    [
        "CkTypeDto.DerivedFromCkTypeId",
        "CkTypeDto.Implements",
        "CkTypeAttributeDto.CkAttributeId",
        "CkTypeAssociationDto.CkRoleId",
        "CkTypeAssociationDto.TargetCkTypeId",
        "CkTypeAssociationDto.TargetCkInterfaceId",
        "CkTypeAssociationDto.TargetCkAttributeIds",
        "CkRecordDto.DerivedFromCkRecordId",
        "CkAttributeDto.ValueCkRecordId",
        "CkAttributeDto.ValueCkEnumId",
        "CkInterfaceDto.Extends",
        "CkInterfaceAttributeDto.CkAttributeId",
        "CkInterfaceAssociationDto.CkRoleId",
        "CkInterfaceAssociationDto.TargetCkTypeId",
        "CkInterfaceAssociationDto.TargetCkInterfaceId",
        "CkMethodParameterDto.ValueCkRecordId",
        "CkMethodParameterDto.ValueCkEnumId",
        "CkMethodResultDto.ValueCkRecordId",
        "CkMethodResultDto.ValueCkEnumId"
    ];

    /// <summary>References that carry no visibility relation, each with the reason.</summary>
    private static readonly Dictionary<string, string> NotWalked = new()
    {
        ["CkTypeAssociationTuple.CkTypeId"] =
            "query-column helper built from the resolved graph (CkTypeQueryColumnCollector); not part of a model file",
        ["CkTypeAssociationTuple.CkAssociationRoleId"] =
            "query-column helper built from the resolved graph (CkTypeQueryColumnCollector); not part of a model file"
    };

    [Fact]
    public void EveryCkReferenceProperty_IsWalkedOrListedWithAReason()
    {
        var found = typeof(CkTypeDto).Assembly.GetTypes()
            .Where(t => t is { IsPublic: true, IsClass: true } &&
                        t.Namespace == typeof(CkTypeDto).Namespace)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(p => IsCkReference(p.PropertyType))
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}")
            .ToHashSet();

        var uncovered = found.Where(p => !Walked.Contains(p) && !NotWalked.ContainsKey(p)).OrderBy(p => p).ToList();
        Assert.True(uncovered.Count == 0,
            "CK reference properties not covered by the visibility walk (CkVisibilityValidator + CkVisibilityIndex): " +
            string.Join(", ", uncovered));

        var stale = Walked.Concat(NotWalked.Keys).Where(p => !found.Contains(p)).OrderBy(p => p).ToList();
        Assert.True(stale.Count == 0, "Listed reference properties that no longer exist: " + string.Join(", ", stale));
    }

    private static bool IsCkReference(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(CkId<>))
        {
            return true;
        }

        return type.IsGenericType && type.GetGenericArguments().Length == 1 &&
               typeof(System.Collections.IEnumerable).IsAssignableFrom(type) &&
               IsCkReference(type.GetGenericArguments()[0]);
    }
}
