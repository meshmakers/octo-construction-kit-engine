using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.BlueprintCatalogs;

/// <summary>
/// Tests for the Blueprint composition data structures.
/// Full integration tests for BlueprintComposer require a catalog setup
/// and are performed in the system tests.
/// </summary>
public class BlueprintComposerTests
{
    #region ComposedBlueprintDto Tests

    [Fact]
    public void ComposedBlueprintDto_WithEmptyCollections_IsValid()
    {
        var blueprintId = new BlueprintId("TestBlueprint", "1.0.0");
        var composed = new ComposedBlueprintDto
        {
            RootBlueprintId = blueprintId,
            CkModelDependencies = new List<CkModelIdVersionRange>(),
            SeedDataReferences = new List<SeedDataReferenceDto>(),
            ResolvedBlueprints = new List<BlueprintMetaRootDto>()
        };

        Assert.Equal(blueprintId, composed.RootBlueprintId);
        Assert.NotNull(composed.CkModelDependencies);
        Assert.NotNull(composed.SeedDataReferences);
        Assert.NotNull(composed.ResolvedBlueprints);
        Assert.Empty(composed.CkModelDependencies);
        Assert.Empty(composed.SeedDataReferences);
        Assert.Empty(composed.ResolvedBlueprints);
    }

    [Fact]
    public void ComposedBlueprintDto_WithData_RetainsValues()
    {
        var blueprintId = new BlueprintId("TestBlueprint", "1.0.0");
        var composed = new ComposedBlueprintDto
        {
            RootBlueprintId = blueprintId,
            CkModelDependencies = new List<CkModelIdVersionRange>
            {
                new("System", "[2.0,)")
            },
            SeedDataReferences = new List<SeedDataReferenceDto>
            {
                new() { BlueprintId = blueprintId, SeedDataPath = "seed.yaml" }
            },
            ResolvedBlueprints = new List<BlueprintMetaRootDto>
            {
                new() { BlueprintId = blueprintId, Description = "Test" }
            }
        };

        Assert.Equal(blueprintId, composed.RootBlueprintId);
        Assert.Single(composed.CkModelDependencies);
        Assert.Single(composed.SeedDataReferences);
        Assert.Single(composed.ResolvedBlueprints);
    }

    #endregion

    #region SeedDataReferenceDto Tests

    [Fact]
    public void SeedDataReferenceDto_StoresValues()
    {
        var blueprintId = new BlueprintId("TestBlueprint", "1.0.0");
        var seedRef = new SeedDataReferenceDto
        {
            BlueprintId = blueprintId,
            SeedDataPath = "seed-data/entities.yaml",
            ResolvedPath = "/full/path/to/seed-data/entities.yaml"
        };

        Assert.Equal(blueprintId, seedRef.BlueprintId);
        Assert.Equal("seed-data/entities.yaml", seedRef.SeedDataPath);
        Assert.Equal("/full/path/to/seed-data/entities.yaml", seedRef.ResolvedPath);
    }

    [Fact]
    public void SeedDataReferenceDto_ResolvedPathIsOptional()
    {
        var blueprintId = new BlueprintId("TestBlueprint", "1.0.0");
        var seedRef = new SeedDataReferenceDto
        {
            BlueprintId = blueprintId,
            SeedDataPath = "seed-data/entities.yaml"
        };

        Assert.Null(seedRef.ResolvedPath);
    }

    #endregion

    #region BlueprintMetaRootDto Tests

    [Fact]
    public void BlueprintMetaRootDto_DefaultConstructor_InitializesLists()
    {
        var dto = new BlueprintMetaRootDto();

        Assert.NotNull(dto.CkModelDependencies);
        Assert.NotNull(dto.ComposedBlueprints);
        Assert.Empty(dto.CkModelDependencies);
        Assert.Empty(dto.ComposedBlueprints);
    }

    [Fact]
    public void BlueprintMetaRootDto_WithDependencies_RetainsValues()
    {
        var dto = new BlueprintMetaRootDto
        {
            BlueprintId = new BlueprintId("Test", "1.0.0"),
            Description = "Test blueprint",
            CkModelDependencies = new List<CkModelIdVersionRange>
            {
                new("System", "[2.0,)"),
                new("Commerce", "[1.0,)")
            }
        };

        Assert.Equal("Test", dto.BlueprintId.Name);
        Assert.Equal("1.0.0", dto.BlueprintId.Version.ToString());
        Assert.Equal("Test blueprint", dto.Description);
        Assert.Equal(2, dto.CkModelDependencies!.Count);
    }

    [Fact]
    public void BlueprintMetaRootDto_WithComposedBlueprints_RetainsValues()
    {
        var dto = new BlueprintMetaRootDto
        {
            BlueprintId = new BlueprintId("Child", "1.0.0"),
            ComposedBlueprints = new List<BlueprintIdVersionRange>
            {
                new("Base", "[1.0,)"),
                new("Common", "[1.0,)")
            }
        };

        Assert.Equal(2, dto.ComposedBlueprints!.Count);
        Assert.Equal("Base", dto.ComposedBlueprints[0].Name);
        Assert.Equal("Common", dto.ComposedBlueprints[1].Name);
    }

    [Fact]
    public void BlueprintMetaRootDto_SchemaUri_IsCorrect()
    {
        var dto = new BlueprintMetaRootDto();

        Assert.Equal("https://schemas.meshmakers.cloud/blueprint-meta.schema.json", dto.SchemaUri);
        Assert.Equal(BlueprintMetaRootDto.BlueprintMetaSchemaUri, dto.SchemaUri);
    }

    #endregion

    #region Dependency Merging Logic Tests

    [Fact]
    public void CkModelDependencies_CanBeCollectedFromMultipleBlueprints()
    {
        var blueprint1 = new BlueprintMetaRootDto
        {
            BlueprintId = new BlueprintId("Base", "1.0.0"),
            CkModelDependencies = new List<CkModelIdVersionRange>
            {
                new("System", "[2.0,)")
            }
        };

        var blueprint2 = new BlueprintMetaRootDto
        {
            BlueprintId = new BlueprintId("Child", "1.0.0"),
            CkModelDependencies = new List<CkModelIdVersionRange>
            {
                new("Commerce", "[1.0,)")
            }
        };

        // Simulate merging
        var merged = new Dictionary<string, CkModelIdVersionRange>();
        foreach (var bp in new[] { blueprint1, blueprint2 })
        {
            if (bp.CkModelDependencies == null) continue;
            foreach (var dep in bp.CkModelDependencies)
            {
                if (!merged.ContainsKey(dep.Name))
                {
                    merged[dep.Name] = dep;
                }
            }
        }

        Assert.Equal(2, merged.Count);
        Assert.True(merged.ContainsKey("System"));
        Assert.True(merged.ContainsKey("Commerce"));
    }

    [Fact]
    public void CkModelDependencies_FirstOccurrenceWins()
    {
        var blueprint1 = new BlueprintMetaRootDto
        {
            BlueprintId = new BlueprintId("Base", "1.0.0"),
            CkModelDependencies = new List<CkModelIdVersionRange>
            {
                new("System", "[2.0,)")
            }
        };

        var blueprint2 = new BlueprintMetaRootDto
        {
            BlueprintId = new BlueprintId("Child", "1.0.0"),
            CkModelDependencies = new List<CkModelIdVersionRange>
            {
                new("System", "[3.0,)") // Same name, different version
            }
        };

        // Simulate merging (first wins)
        var merged = new Dictionary<string, CkModelIdVersionRange>();
        foreach (var bp in new[] { blueprint1, blueprint2 })
        {
            if (bp.CkModelDependencies == null) continue;
            foreach (var dep in bp.CkModelDependencies)
            {
                if (!merged.ContainsKey(dep.Name))
                {
                    merged[dep.Name] = dep;
                }
            }
        }

        Assert.Single(merged);
        Assert.Equal("[2.0,)", merged["System"].ModelVersionRange.ToString());
    }

    #endregion

    #region Circular Reference Detection Helper Tests

    [Fact]
    public void VisitedSet_DetectsCircularReference()
    {
        var visited = new HashSet<string>();
        var blueprint1 = "BlueprintA-1.0.0";
        var blueprint2 = "BlueprintB-1.0.0";

        // First visit
        Assert.DoesNotContain(blueprint1, visited);
        visited.Add(blueprint1);
        Assert.Contains(blueprint1, visited);

        // Second visit should be detected as circular
        Assert.Contains(blueprint1, visited);

        // Different blueprint is not circular
        Assert.DoesNotContain(blueprint2, visited);
    }

    #endregion
}
