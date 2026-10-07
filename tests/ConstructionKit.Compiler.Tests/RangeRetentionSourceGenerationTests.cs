using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.SourceGeneration;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     D1 (CK v2 Phase 0 E2E): with <c>OctoCkRangeRetention=true</c> the CK source generator failed with
///     <c>OM1003 CkAttributeId 'System@2/Enabled-1' not found in CkCache</c> — the compiled yaml holds
///     major-qualified references, the compile cache holds concrete versions. Reproduces the generator's steps
///     (restore the cache file, deserialize the compiled yaml, generate each type) on real compiler output.
/// </summary>
public sealed class RangeRetentionSourceGenerationTests : IDisposable
{
    private readonly CkCompileFixture _fixture =
        new(s => s.Configure<CkCompilerOptions>(o => o.RangeRetention = true));

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task RangeRetainingModel_GeneratesAfterBindingToTheCompileCache()
    {
        await _fixture.CompileAndPublishAsync(_fixture.WriteSystemModel("2.5.0", withExtraAttribute: true));
        var source = _fixture.WriteSource("dep", "Dep-1.0.0", ["System-[2.5,3.0)"], new Dictionary<string, string>
        {
            ["types/thing.yaml"] = "types:\n  - typeId: Thing\n    derivedFromCkTypeId: ${System}/Entity\n" +
                                   "    attributes:\n      - id: ${System}/Extra\n        name: Label\n        isOptional: true\n"
        });
        var outputDir = Path.Combine(_fixture.Root, "out");
        var cacheDir = Path.Combine(_fixture.Root, "cache");
        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(cacheDir);

        var result = await _fixture.Services.GetRequiredService<ICompilerService>()
            .CompileAsync(source, outputDir, cacheDir);

        // Generator steps (CkSourceGenerator.GenerateCode).
        var cacheService = _fixture.Services.GetRequiredService<ICkCacheService>();
        const string tenantId = "generator";
        cacheService.CreateTenant(tenantId);
        cacheService.RestoreCache(tenantId,
            await File.ReadAllTextAsync(result.CompiledModelCacheFilePath!, TestContext.Current.CancellationToken));
        var operationResult = new OperationResult();
        var compiled = _fixture.Services.GetRequiredService<ICkYamlSerializer>().DeserializeCompiledModelRoot(
            await File.ReadAllTextAsync(result.CompiledModelFile, TestContext.Current.CancellationToken),
            result.CompiledModelFile, operationResult);
        Assert.Equal("System@2/Extra-1", compiled.Types!.Single().Attributes!.Single().CkAttributeId.FullName);

        // Without binding the lookup fails exactly like the E2E build did.
        var unbound = _fixture.Services.GetRequiredService<ICkYamlSerializer>().DeserializeCompiledModelRoot(
            await File.ReadAllTextAsync(result.CompiledModelFile, TestContext.Current.CancellationToken),
            result.CompiledModelFile, new OperationResult());
        Assert.ThrowsAny<Exception>(() => CkTypeCodeGenerator.Instance.Generate("Ns", unbound.ModelId,
            unbound.Types!.Single(), tenantId, cacheService));

        Assert.Equal(2, CkGenerationModelBinder.BindToCache(compiled, cacheService, tenantId));
        var code = CkTypeCodeGenerator.Instance.Generate("Ns", compiled.ModelId, compiled.Types!.Single(), tenantId,
            cacheService);

        Assert.Contains("public partial class RtThing", code);
        Assert.Contains("Label", code);
    }
}
