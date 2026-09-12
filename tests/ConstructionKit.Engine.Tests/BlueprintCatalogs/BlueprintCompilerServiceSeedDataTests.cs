using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.BlueprintCatalogs;

/// <summary>
///     Covers what <c>octo-bpm validate</c> reports for a blueprint whose seed is split across
///     several files (AB#4758): every declared file must exist, no rtId may be declared twice
///     across files, and a stray YAML file nobody references is flagged.
/// </summary>
public class BlueprintCompilerServiceSeedDataTests : IDisposable
{
    private readonly BlueprintCompilerService _compiler =
        new(new BlueprintYamlSerializer(), NullLogger<BlueprintCompilerService>.Instance);

    private readonly string _blueprintDirectory =
        Path.Combine(Path.GetTempPath(), $"bp-seed-split-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_blueprintDirectory))
        {
            Directory.Delete(_blueprintDirectory, true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ValidateAsync_SplitSeedWithDistinctRtIds_IsValid()
    {
        WriteManifest("""
            $schema: https://schemas.meshmakers.cloud/blueprint-meta.schema.json
            blueprintId: TestBlueprint-1.0.0
            seedDataPaths:
              - seed-data/configurations/base.yaml
              - seed-data/data-flows/camt053.yaml
            """);
        WriteSeed("seed-data/configurations/base.yaml", "aaaa00000000000000000001");
        WriteSeed("seed-data/data-flows/camt053.yaml", "aaaa00000000000000000002");

        var operationResult = new OperationResult();
        var meta = await _compiler.ValidateAsync(_blueprintDirectory, operationResult, TestContext.Current.CancellationToken);

        Assert.Equal("TestBlueprint-1.0.0", meta.BlueprintId.FullName);
        Assert.Empty(operationResult.Messages);
        Assert.Equal(
            ["seed-data/configurations/base.yaml", "seed-data/data-flows/camt053.yaml"],
            BlueprintSeedData.ResolvePaths(meta));
    }

    [Fact]
    public async Task ValidateAsync_MissingFileInSplitSeed_IsError()
    {
        // Importing only the files that happen to exist would provision a partially seeded tenant
        // and still report success, so a missing file is fatal once several are declared.
        WriteManifest("""
            $schema: https://schemas.meshmakers.cloud/blueprint-meta.schema.json
            blueprintId: TestBlueprint-1.0.0
            seedDataPaths:
              - seed-data/configurations/base.yaml
              - seed-data/data-flows/gone.yaml
            """);
        WriteSeed("seed-data/configurations/base.yaml", "aaaa00000000000000000001");

        var operationResult = new OperationResult();

        await Assert.ThrowsAsync<BlueprintCatalogException>(
            () => _compiler.ValidateAsync(_blueprintDirectory, operationResult, TestContext.Current.CancellationToken));

        Assert.Contains(operationResult.Messages,
            m => m.MessageLevel == MessageLevel.Error
                 && m.MessageText.Contains("seed-data/data-flows/gone.yaml"));
    }

    [Fact]
    public async Task ValidateAsync_MissingFileInSingleFileForm_StaysAWarning()
    {
        // Unchanged behaviour for the original single-file form.
        WriteManifest("""
            $schema: https://schemas.meshmakers.cloud/blueprint-meta.schema.json
            blueprintId: TestBlueprint-1.0.0
            seedDataPath: seed-data/entities.yaml
            """);

        var operationResult = new OperationResult();
        await _compiler.ValidateAsync(_blueprintDirectory, operationResult, TestContext.Current.CancellationToken);

        Assert.False(operationResult.HasErrors);
        Assert.Contains(operationResult.Messages,
            m => m.MessageLevel == MessageLevel.Warning
                 && m.MessageText.Contains("seed-data/entities.yaml"));
    }

    [Fact]
    public async Task ValidateAsync_DuplicateRtIdAcrossFiles_IsError()
    {
        WriteManifest("""
            $schema: https://schemas.meshmakers.cloud/blueprint-meta.schema.json
            blueprintId: TestBlueprint-1.0.0
            seedDataPaths:
              - seed-data/configurations/base.yaml
              - seed-data/data-flows/camt053.yaml
            """);
        WriteSeed("seed-data/configurations/base.yaml", "aaaa00000000000000000001");
        WriteSeed("seed-data/data-flows/camt053.yaml", "aaaa00000000000000000001");

        var operationResult = new OperationResult();

        await Assert.ThrowsAsync<BlueprintCatalogException>(
            () => _compiler.ValidateAsync(_blueprintDirectory, operationResult, TestContext.Current.CancellationToken));

        Assert.Contains(operationResult.Messages,
            m => m.MessageLevel == MessageLevel.Error
                 && m.MessageText.Contains("Duplicate entity 'System/Entity-1@aaaa00000000000000000001'")
                 && m.MessageText.Contains("seed-data/configurations/base.yaml"));
    }

    [Fact]
    public async Task ValidateAsync_DuplicateRtIdInsideOneFile_IsOnlyAWarning()
    {
        // Pre-existing single-file blueprints must keep validating; the importer already reports
        // this case at install time.
        WriteManifest("""
            $schema: https://schemas.meshmakers.cloud/blueprint-meta.schema.json
            blueprintId: TestBlueprint-1.0.0
            seedDataPath: seed-data/entities.yaml
            """);
        WriteSeed("seed-data/entities.yaml", "aaaa00000000000000000001", "aaaa00000000000000000001");

        var operationResult = new OperationResult();
        await _compiler.ValidateAsync(_blueprintDirectory, operationResult, TestContext.Current.CancellationToken);

        Assert.False(operationResult.HasErrors);
        Assert.Contains(operationResult.Messages,
            m => m.MessageLevel == MessageLevel.Warning
                 && m.MessageText.Contains("Duplicate entity 'System/Entity-1@aaaa00000000000000000001'"));
    }

    [Fact]
    public async Task ValidateAsync_SameRtIdUnderDifferentCkTypes_IsOnlyAWarning()
    {
        // Entities are keyed by CK type plus rtId — the same id under two types is legal, and
        // shipped blueprints do it. Rejecting it would break them; saying nothing would hide a
        // genuinely error-prone shape.
        WriteManifest("""
            $schema: https://schemas.meshmakers.cloud/blueprint-meta.schema.json
            blueprintId: TestBlueprint-1.0.0
            seedDataPaths:
              - seed-data/identity/roles.yaml
              - seed-data/data-flows/camt053.yaml
            """);
        WriteSeedOfType("seed-data/identity/roles.yaml", "System.Identity/Role",
            "aaaa00000000000000000001");
        WriteSeedOfType("seed-data/data-flows/camt053.yaml", "System.Communication/Pipeline",
            "aaaa00000000000000000001");

        var operationResult = new OperationResult();
        await _compiler.ValidateAsync(_blueprintDirectory, operationResult, TestContext.Current.CancellationToken);

        Assert.False(operationResult.HasErrors);
        Assert.Contains(operationResult.Messages,
            m => m.MessageLevel == MessageLevel.Warning
                 && m.MessageText.Contains("aaaa00000000000000000001")
                 && m.MessageText.Contains("System.Identity/Role")
                 && m.MessageText.Contains("System.Communication/Pipeline"));
    }

    [Fact]
    public async Task ValidateAsync_UnreferencedSeedFile_IsReported()
    {
        // Forgetting to add a newly created file to seedDataPaths is the one failure mode the split
        // form introduces; it would otherwise pass as a silently smaller install.
        WriteManifest("""
            $schema: https://schemas.meshmakers.cloud/blueprint-meta.schema.json
            blueprintId: TestBlueprint-1.0.0
            seedDataPaths:
              - seed-data/configurations/base.yaml
            """);
        WriteSeed("seed-data/configurations/base.yaml", "aaaa00000000000000000001");
        WriteSeed("seed-data/data-flows/forgotten.yaml", "aaaa00000000000000000002");

        var operationResult = new OperationResult();
        await _compiler.ValidateAsync(_blueprintDirectory, operationResult, TestContext.Current.CancellationToken);

        Assert.False(operationResult.HasErrors);
        Assert.Contains(operationResult.Messages,
            m => m.MessageLevel == MessageLevel.Warning
                 && m.MessageText.Contains("seed-data/data-flows/forgotten.yaml")
                 && m.MessageText.Contains("not referenced"));
    }

    [Fact]
    public async Task ValidateAsync_BothSeedDataForms_WarnsButLoadsBoth()
    {
        WriteManifest("""
            $schema: https://schemas.meshmakers.cloud/blueprint-meta.schema.json
            blueprintId: TestBlueprint-1.0.0
            seedDataPath: seed-data/entities.yaml
            seedDataPaths:
              - seed-data/data-flows/camt053.yaml
            """);
        WriteSeed("seed-data/entities.yaml", "aaaa00000000000000000001");
        WriteSeed("seed-data/data-flows/camt053.yaml", "aaaa00000000000000000002");

        var operationResult = new OperationResult();
        await _compiler.ValidateAsync(_blueprintDirectory, operationResult, TestContext.Current.CancellationToken);

        Assert.False(operationResult.HasErrors);
        Assert.Contains(operationResult.Messages,
            m => m.MessageLevel == MessageLevel.Warning
                 && m.MessageText.Contains("'seedDataPath' and 'seedDataPaths'"));
    }

    private void WriteManifest(string yaml)
    {
        Directory.CreateDirectory(_blueprintDirectory);
        File.WriteAllText(Path.Combine(_blueprintDirectory, "blueprint.yaml"), yaml);
    }

    private void WriteSeed(string relativePath, params string[] rtIds)
    {
        WriteSeedOfType(relativePath, "System/Entity-1", rtIds);
    }

    private void WriteSeedOfType(string relativePath, string ckTypeId, params string[] rtIds)
    {
        var fullPath = Path.Combine(_blueprintDirectory,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var entities = string.Join(Environment.NewLine, rtIds.Select(rtId => $"""
            - rtId: {rtId}
              ckTypeId: {ckTypeId}
            """));

        File.WriteAllText(fullPath, $"""
            $schema: https://schemas.meshmakers.cloud/runtime-model.schema.json
            dependencies:
              - System-2.0.0
            entities:
            {entities}
            """);
    }
}
