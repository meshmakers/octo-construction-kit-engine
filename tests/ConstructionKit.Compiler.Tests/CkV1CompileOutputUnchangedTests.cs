using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     CK v2 Phase 0 (AB#5584): a <c>ckLanguage: 1</c> model compiles byte-identical to the engine before CK v2 —
///     both the compiled model and the CK cache file. The golden files were produced by the pre-CK-v2 engine
///     (see <c>sampleData/v1Golden/README.md</c>).
/// </summary>
public sealed class CkV1CompileOutputUnchangedTests : IDisposable
{
    private const string GoldenRoot = "sampleData/v1Golden";
    private readonly CkCompileFixture _fixture = new();

    public void Dispose()
    {
        _fixture.Dispose();
    }

    [Fact]
    public async Task System_2_5_0_CompiledModelAndCache_AreByteIdenticalToPreCkV2Engine()
    {
        var outputDir = Path.Combine(_fixture.Root, "out");
        Directory.CreateDirectory(outputDir);
        var sourceDir = Path.GetFullPath(Path.Combine(GoldenRoot, "system-2.5.0"));

        var result = await _fixture.Services.GetRequiredService<ICompilerService>()
            .CompileAsync(sourceDir, outputDir, outputDir, new OperationResult());

        Assert.Equal(await File.ReadAllTextAsync(Path.Combine(GoldenRoot, "expected", "ck-system-2.yaml"),
                TestContext.Current.CancellationToken),
            await File.ReadAllTextAsync(result.CompiledModelFile, TestContext.Current.CancellationToken));
        Assert.Equal(await File.ReadAllTextAsync(Path.Combine(GoldenRoot, "expected", "ck-system-2.cache.json"),
                TestContext.Current.CancellationToken),
            await File.ReadAllTextAsync(result.CompiledModelCacheFilePath!, TestContext.Current.CancellationToken));
    }
}
