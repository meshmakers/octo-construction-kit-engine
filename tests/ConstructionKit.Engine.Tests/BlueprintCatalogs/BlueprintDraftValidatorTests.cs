using System.IO.Compression;
using System.Text;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.BlueprintCatalogs;

/// <summary>
///     The shared blueprint draft validator - one finding class per test, stable codes, the CK dependency
///     range check against the catalogs and the zip source.
/// </summary>
public class BlueprintDraftValidatorTests
{
    private const string GoodManifest = """
        blueprintId: Draft-1.0.0
        description: a draft
        ckModelDependencies:
          - System-[2.0,3.0)
        seedDataPath: seed-data/entities.yaml
        """;

    private const string GoodSeed = """
        dependencies:
          - System-2.0.0
        entities:
          - rtId: aaaa00000000000000000001
            ckTypeId: System/Entity
        """;

    private readonly ICatalogManager _catalogs = A.Fake<ICatalogManager>();

    public BlueprintDraftValidatorTests()
    {
        A.CallTo(() => _catalogs.IsExistingAsync(A<CkModelIdVersionRange>._, A<object?>._))
            .Returns(new ModelExistingResult { Exists = true, ModelId = new CkModelId("System", "2.9.0") });
    }

    private BlueprintDraftValidator CreateValidator() => new(new BlueprintYamlSerializer(), null, _catalogs);

    private static InMemoryBlueprintFileSource Source(params (string Path, string Content)[] files) =>
        new(files.Select(f => new KeyValuePair<string, string>(f.Path, f.Content)));

    private static readonly BlueprintValidationOptions AllCatalogs = new() { CkModelCatalogs = [] };

    [Fact]
    public async Task GoodDraft_HasNoFindings()
    {
        var result = await CreateValidator().ValidateAsync(
            Source(("blueprint.yaml", GoodManifest), ("seed-data/entities.yaml", GoodSeed)), AllCatalogs,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Empty(result.Findings);
        Assert.Equal("Draft-1.0.0", result.BlueprintMeta!.BlueprintId.FullName);
    }

    [Fact]
    public async Task WithoutCatalogs_DependencyRangesAreNotChecked_Info165()
    {
        var result = await CreateValidator().ValidateAsync(
            Source(("blueprint.yaml", GoodManifest), ("seed-data/entities.yaml", GoodSeed)), null,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        var info = Assert.Single(result.Findings);
        Assert.Equal(BlueprintDraftValidationCodes.CkModelDependenciesNotChecked, info.MessageNumber);
        Assert.Equal(MessageLevel.Info, info.MessageLevel);
        A.CallTo(() => _catalogs.IsExistingAsync(A<CkModelIdVersionRange>._, A<object?>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task MissingManifest_Error149()
    {
        var result = await CreateValidator().ValidateAsync(Source(("seed-data/entities.yaml", GoodSeed)), AllCatalogs,
            TestContext.Current.CancellationToken);

        AssertSingleError(result, BlueprintDraftValidationCodes.ManifestMissing);
        Assert.Null(result.BlueprintMeta);
    }

    [Fact]
    public async Task UnparsableManifest_Error150()
    {
        var result = await CreateValidator().ValidateAsync(Source(("blueprint.yaml", "blueprintId: [unclosed")),
            AllCatalogs, TestContext.Current.CancellationToken);

        AssertSingleError(result, BlueprintDraftValidationCodes.ManifestUnreadable);
    }

    [Fact]
    public async Task MissingBlueprintId_Error151()
    {
        var result = await CreateValidator().ValidateAsync(Source(("blueprint.yaml", "description: no id\n")),
            AllCatalogs, TestContext.Current.CancellationToken);

        AssertSingleError(result, BlueprintDraftValidationCodes.BlueprintIdMissing);
    }

    [Fact]
    public async Task MissingSingleSeedFile_Warning152_MissingOneOfSeveral_Error153()
    {
        var single = await CreateValidator().ValidateAsync(Source(("blueprint.yaml", GoodManifest)), AllCatalogs,
            TestContext.Current.CancellationToken);
        Assert.True(single.IsValid);
        Assert.Contains(single.Findings, f => f.MessageNumber == BlueprintDraftValidationCodes.SeedDataFileMissing
                                              && f.Parameters["seedDataPath"] == "seed-data/entities.yaml");

        var several = await CreateValidator().ValidateAsync(Source(("blueprint.yaml", """
                blueprintId: Draft-1.0.0
                seedDataPaths:
                  - seed-data/a.yaml
                  - seed-data/b.yaml
                """), ("seed-data/a.yaml", GoodSeed)), AllCatalogs, TestContext.Current.CancellationToken);
        AssertSingleError(several, BlueprintDraftValidationCodes.SeedDataFileOfSeveralMissing);
    }

    [Fact]
    public async Task UnparsableSeed_Error155()
    {
        var result = await CreateValidator().ValidateAsync(
            Source(("blueprint.yaml", GoodManifest), ("seed-data/entities.yaml", "entities: [unclosed")), AllCatalogs,
            TestContext.Current.CancellationToken);

        AssertSingleError(result, BlueprintDraftValidationCodes.SeedDataFileUnparsable);
    }

    [Fact]
    public async Task DuplicateEntityAcrossFiles_Error157_UnreferencedFile_Warning158()
    {
        var result = await CreateValidator().ValidateAsync(Source(("blueprint.yaml", """
                blueprintId: Draft-1.0.0
                seedDataPaths:
                  - seed-data/a.yaml
                  - seed-data/b.yaml
                """), ("seed-data/a.yaml", GoodSeed), ("seed-data/b.yaml", GoodSeed),
            ("seed-data/forgotten.yaml", GoodSeed)), AllCatalogs, TestContext.Current.CancellationToken);

        AssertSingleError(result, BlueprintDraftValidationCodes.SeedEntityDuplicateAcrossFiles);
        Assert.Contains(result.Findings, f => f.MessageNumber == BlueprintDraftValidationCodes.SeedDataFileUnreferenced
                                              && f.MessageLevel == MessageLevel.Warning);
    }

    [Fact]
    public async Task UnknownDependencyRange_Error163_WithModelId()
    {
        A.CallTo(() => _catalogs.IsExistingAsync(A<CkModelIdVersionRange>._, A<object?>._))
            .Returns(new ModelExistingResult { Exists = false });

        var result = await CreateValidator().ValidateAsync(Source(("blueprint.yaml", """
                blueprintId: Draft-1.0.0
                ckModelDependencies:
                  - Acme.Unknown-[7.0,8.0)
                """)), AllCatalogs, TestContext.Current.CancellationToken);

        var error = AssertSingleError(result, BlueprintDraftValidationCodes.CkModelDependencyNotInCatalogs);
        Assert.Equal("Acme.Unknown", error.Parameters["modelId"]);
        Assert.Equal("[7.0,8.0)", error.Parameters["versionRange"]);
    }

    [Theory]
    [InlineData("'-[1.0,2.0)'")] // found on the lane stack: read as a model named '-[1.0,2.0)', reported as 163 + 161
    [InlineData("''")]
    public async Task DependencyWithoutName_Error162_Once_NotCheckedAgainstCatalogs(string entry)
    {
        var result = await CreateValidator().ValidateAsync(Source(("blueprint.yaml", $"""
                blueprintId: Draft-1.0.0
                ckModelDependencies:
                  - System-[2.0,3.0)
                  - {entry}
                """)), AllCatalogs, TestContext.Current.CancellationToken);

        AssertSingleError(result, BlueprintDraftValidationCodes.CkModelDependencyNameMissing);
        Assert.DoesNotContain(result.Findings,
            f => f.MessageNumber == BlueprintDraftValidationCodes.CkModelDependencyNotInCatalogs);
        A.CallTo(() => _catalogs.IsExistingAsync(A<CkModelIdVersionRange>.That.Matches(d => d.Name != "System"),
            A<object?>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task DependencyRangeWithoutMinimum_Error164_WithModelId()
    {
        var result = await CreateValidator().ValidateAsync(Source(("blueprint.yaml", """
                blueprintId: Draft-1.0.0
                ckModelDependencies:
                  - System-(,3.0)
                """)), AllCatalogs, TestContext.Current.CancellationToken);

        var error = AssertSingleError(result, BlueprintDraftValidationCodes.CkModelDependencyNoMinimumVersion);
        Assert.Equal("System", error.Parameters["modelId"]);
        A.CallTo(() => _catalogs.IsExistingAsync(A<CkModelIdVersionRange>._, A<object?>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task NamedCatalogs_AreQueriedOneByOne()
    {
        A.CallTo(() => _catalogs.IsExistingAsync("PublicGitHubCatalog", A<CkModelIdVersionRange>._, A<object?>._))
            .Returns(new ModelExistingResult { Exists = false });
        A.CallTo(() => _catalogs.IsExistingAsync("LocalFileSystemCatalog", A<CkModelIdVersionRange>._, A<object?>._))
            .Returns(new ModelExistingResult { Exists = true });

        var result = await CreateValidator().ValidateAsync(Source(("blueprint.yaml", GoodManifest)),
            new BlueprintValidationOptions { CkModelCatalogs = ["PublicGitHubCatalog", "LocalFileSystemCatalog"] },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        A.CallTo(() => _catalogs.IsExistingAsync(A<CkModelIdVersionRange>._, A<object?>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task UnreachableCatalog_Warning166_NotAnError()
    {
        A.CallTo(() => _catalogs.IsExistingAsync(A<CkModelIdVersionRange>._, A<object?>._))
            .Returns(new ModelExistingResult { Exists = false, SourceUnreachable = true });

        var result = await CreateValidator().ValidateAsync(Source(("blueprint.yaml", GoodManifest)), AllCatalogs,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Contains(result.Findings,
            f => f.MessageNumber == BlueprintDraftValidationCodes.CkModelDependencyNotCheckable);
    }

    [Fact]
    public async Task IgnoredModel_IsNotCheckedAgainstCatalogs()
    {
        A.CallTo(() => _catalogs.IsExistingAsync(A<CkModelIdVersionRange>._, A<object?>._))
            .Returns(new ModelExistingResult { Exists = false });

        var result = await CreateValidator().ValidateAsync(Source(("blueprint.yaml", GoodManifest)),
            new BlueprintValidationOptions { CkModelCatalogs = [], IgnoredCkModels = ["System"] },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ZipOfPackedFolder_IsReadFromTheBlueprintRoot()
    {
        using var zip = Zip(("Draft-1.0.0/blueprint.yaml", GoodManifest),
            ("Draft-1.0.0/seed-data/entities.yaml", GoodSeed));

        var source = InMemoryBlueprintFileSource.FromZip(zip);
        var result = await CreateValidator().ValidateAsync(source, AllCatalogs, TestContext.Current.CancellationToken);

        Assert.Empty(result.Findings);
        Assert.Contains("seed-data/entities.yaml", source.Paths);
    }

    [Fact]
    public void ZipWithTraversalPath_IsRefused()
    {
        using var zip = Zip(("blueprint.yaml", GoodManifest), ("../evil.yaml", "x"));

        Assert.Throws<InvalidDataException>(() => InMemoryBlueprintFileSource.FromZip(zip));
    }

    [Fact]
    public void ZipOverTheSizeLimit_IsRefused()
    {
        using var zip = Zip(("blueprint.yaml", GoodManifest));

        Assert.Throws<InvalidDataException>(() => InMemoryBlueprintFileSource.FromZip(zip, maxUnpackedBytes: 10));
    }

    [Fact]
    public void InstallBlockingCodes_DoNotContainWarningsOrTheSecretLint()
    {
        Assert.DoesNotContain(BlueprintDraftValidationCodes.SeedDataFileMissing,
            BlueprintDraftValidationCodes.InstallBlockingCodes);
        Assert.DoesNotContain(BlueprintDraftValidationCodes.SeedSecretValue,
            BlueprintDraftValidationCodes.InstallBlockingCodes);
        Assert.DoesNotContain(BlueprintDraftValidationCodes.SeedEntityDuplicateAcrossFiles,
            BlueprintDraftValidationCodes.InstallBlockingCodes);
        Assert.DoesNotContain(BlueprintDraftValidationCodes.CkModelDependencyNotInCatalogs,
            BlueprintDraftValidationCodes.InstallBlockingCodes);
    }

    private static OperationMessage AssertSingleError(BlueprintDraftValidationResult result, int code)
    {
        Assert.False(result.IsValid);
        var error = Assert.Single(result.Findings,
            f => f.MessageLevel is MessageLevel.Error or MessageLevel.FatalError);
        Assert.Equal(code, error.MessageNumber);
        return error;
    }

    private static MemoryStream Zip(params (string Path, string Content)[] files)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }
}
