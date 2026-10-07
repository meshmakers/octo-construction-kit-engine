using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     CK v2 Phase 0 (AB#5584): a <c>ckLanguage: 1</c> model compiles byte-identical to the engine of
///     <c>main</c> before CK v2 — the compiled model, the CK cache file and the published catalog JSON, for a model
///     without dependencies (System) and one with a dependency (GoldenDependent, review L18: exercises the changed
///     dependency resolvers and reference handling). The golden files were produced by octo-ckc built from
///     <c>main</c> (see <c>sampleData/v1Golden/README.md</c>).
/// </summary>
public sealed class CkV1CompileOutputUnchangedTests : IDisposable
{
    private const string GoldenRoot = "sampleData/v1Golden";
    private readonly CkCompileFixture _fixture = new();

    public void Dispose()
    {
        _fixture.Dispose();
    }

    private async Task<CompileResult> CompileAndPublishLikeOctoCkcAsync(string sourceFolder)
    {
        var outputDir = Path.Combine(_fixture.Root, "out", sourceFolder);
        Directory.CreateDirectory(outputDir);
        var result = await _fixture.Services.GetRequiredService<ICompilerService>().CompileAsync(
            Path.GetFullPath(Path.Combine(GoldenRoot, sourceFolder)), outputDir, outputDir, new OperationResult());

        // octo-ckc -c publish: deserialize the compiled yaml and publish it.
        var operationResult = new OperationResult();
        await using var stream = File.OpenRead(result.CompiledModelFile);
        var compiled = await _fixture.Services.GetRequiredService<ICkSerializer>()
            .DeserializeCompiledModelRootAsync(stream, result.CompiledModelFile, operationResult);
        Assert.False(operationResult.HasErrors);
        await _fixture.Services.GetRequiredService<ICatalogService>().PublishAsync(LocalFileSystemCatalog.Name,
            compiled, new OriginFileResolver(result.CompiledModelFile), true, operationResult);
        return result;
    }

    private static async Task AssertSameAsync(string expectedRelativePath, string actualPath)
    {
        Assert.Equal(await File.ReadAllTextAsync(Path.Combine(GoldenRoot, "expected", expectedRelativePath),
                TestContext.Current.CancellationToken),
            await File.ReadAllTextAsync(actualPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task System_2_5_0_CompiledModelCacheAndCatalogJson_AreByteIdenticalToMain()
    {
        var result = await CompileAndPublishLikeOctoCkcAsync("system-2.5.0");

        await AssertSameAsync("ck-system-2.yaml", result.CompiledModelFile);
        await AssertSameAsync("ck-system-2.cache.json", result.CompiledModelCacheFilePath!);
        await AssertSameAsync(Path.Combine("catalog", "ck-system-2.5.0.json"),
            Path.Combine(_fixture.CatalogDir, "ck-models", "v2", "s", "System", "2", "ck-system-2.5.0.json"));
    }

    [Fact]
    public async Task ModelWithDependency_CompiledModelCacheAndCatalogJson_AreByteIdenticalToMain()
    {
        await CompileAndPublishLikeOctoCkcAsync("system-2.5.0");

        var result = await CompileAndPublishLikeOctoCkcAsync("dependent-1.0.0");

        await AssertSameAsync("ck-goldendependent.yaml", result.CompiledModelFile);
        await AssertSameAsync("ck-goldendependent.cache.json", result.CompiledModelCacheFilePath!);
        await AssertSameAsync(Path.Combine("catalog", "ck-goldendependent-1.0.0.json"),
            Path.Combine(_fixture.CatalogDir, "ck-models", "v2", "g", "GoldenDependent", "1",
                "ck-goldendependent-1.0.0.json"));
    }
}
