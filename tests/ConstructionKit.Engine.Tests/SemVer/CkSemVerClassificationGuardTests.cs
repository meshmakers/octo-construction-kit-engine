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
        typeof(CkAttributeMetaDataDto)
    ];

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
