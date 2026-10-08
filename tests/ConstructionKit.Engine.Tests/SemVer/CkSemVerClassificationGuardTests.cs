using System.Reflection;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer;

/// <summary>
///     Guard against silently unclassified schema evolution: every public property of the
///     element DTOs must be registered in <see cref="CkModelDiffService.AccountedProperties" />
///     (compared or knowingly excluded). When this test fails, add a diff implementation and a
///     classification rule for the new property — and extend docs/ck-semver-rules.md.
/// </summary>
public class CkSemVerClassificationGuardTests
{
    private static readonly Type[] KnownDtoTypes =
    [
        typeof(CkCompiledModelRoot),
        typeof(CkModelRootBase),
        typeof(CkModelPropertiesDto),
        typeof(CkCompiledTypeDto),
        typeof(CkTypeDto),
        typeof(CkTypeWithAttributesDto),
        typeof(CkAttributeDto),
        typeof(CkEnumDto),
        typeof(CkEnumValueDto),
        typeof(CkRecordDto),
        typeof(CkAssociationRoleDto),
        typeof(CkTypeAttributeDto),
        typeof(CkTypeAssociationDto),
        typeof(CkTypeIndexDto),
        typeof(CkIndexFieldsDto),
        typeof(CkAttributeMetaDataDto),
        // CK v2 (AB#5667 / AB#5669)
        typeof(CkInterfaceDto),
        typeof(CkInterfaceAttributeDto),
        typeof(CkInterfaceAssociationDto),
        typeof(CkMethodDto),
        typeof(CkMethodParameterDto),
        typeof(CkMethodResultDto),
        typeof(CkMethodErrorDto),
        typeof(CkMethodAuthorizationDto),
        typeof(CkMethodExecutionDto)
    ];

    /// <summary>
    ///     DTO types that are not part of a compiled model's public surface and therefore not diffed:
    ///     source/input roots, the cache root, the model-config file, the range-retention dependency entry
    ///     (diffed with DependencyRanges in Phase 2) and an internal tuple.
    /// </summary>
    private static readonly Type[] NotDiffedDtoTypes =
    [
        typeof(CkCacheRoot),
        typeof(CkElementsRootDto),
        typeof(CkMetaRootDto),
        typeof(CkModelCompileCandidate),
        typeof(CkModelConfigDto),
        typeof(CkModelDependencyDto),
        typeof(CkTypeAssociationTuple)
    ];

    /// <summary>
    ///     F1.1-S1 (AB#5904): a NEW DTO type (not just a new property) must not slip past the diff either — every
    ///     public class/record in the DataTransferObjects namespace is either checked above or consciously excluded.
    /// </summary>
    [Fact]
    public void EveryDtoType_IsKnownOrConsciouslyExcluded()
    {
        var unclassified = typeof(CkCompiledModelRoot).Assembly.GetTypes()
            .Where(t => t.IsPublic && t.IsClass && t.Namespace == typeof(CkCompiledModelRoot).Namespace &&
                        !t.IsSubclassOf(typeof(Attribute)) && !(t.IsAbstract && t.IsSealed))
            .Where(t => !KnownDtoTypes.Contains(t) && !NotDiffedDtoTypes.Contains(t))
            .Select(t => t.Name)
            .ToList();

        Assert.True(unclassified.Count == 0,
            "DTO types neither registered in KnownDtoTypes/CkModelDiffService.AccountedProperties nor excluded: " +
            string.Join(", ", unclassified));
    }

    public static TheoryData<Type> ElementDtoTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var dtoType in KnownDtoTypes)
        {
            data.Add(dtoType);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ElementDtoTypes))]
    public void EveryDtoProperty_IsAccountedForInTheDiff(Type dtoType)
    {
        Assert.True(CkModelDiffService.AccountedProperties.TryGetValue(dtoType, out var accountedProperties),
            $"DTO type '{dtoType.Name}' is not registered in CkModelDiffService.AccountedProperties.");

        var declaredProperties = dtoType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(p => p.Name)
            .ToHashSet();

        var unaccounted = declaredProperties.Except(accountedProperties!).ToList();
        Assert.True(unaccounted.Count == 0,
            $"Properties of '{dtoType.Name}' without a diff/classification decision: " +
            $"{string.Join(", ", unaccounted)}. Add a diff implementation and a classification rule " +
            "(or a documented exclusion) and extend docs/ck-semver-rules.md.");

        var stale = accountedProperties!.Except(declaredProperties).ToList();
        Assert.True(stale.Count == 0,
            $"AccountedProperties of '{dtoType.Name}' references unknown properties " +
            $"(renamed or removed?): {string.Join(", ", stale)}.");
    }

    [Fact]
    public void AccountedPropertiesRegistry_CoversExactlyTheKnownDtoTypes()
    {
        var expected = KnownDtoTypes.ToHashSet();
        var actual = CkModelDiffService.AccountedProperties.Keys.ToHashSet();

        Assert.True(expected.SetEquals(actual),
            "The DTO type closure of the diff changed. Update the guard test's type list and " +
            "make a conscious diff/classification decision for new types. " +
            $"Missing in registry: {string.Join(", ", expected.Except(actual).Select(t => t.Name))}; " +
            $"unexpected in registry: {string.Join(", ", actual.Except(expected).Select(t => t.Name))}.");
    }
}
