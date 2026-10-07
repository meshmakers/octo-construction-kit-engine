using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     AB#5661: an unsatisfiable dependency fails fast with the versions the catalogs actually know, so a
///     stale or out-of-order build (StreamData compiled before its sibling System was published) is
///     diagnosable from the message alone.
/// </summary>
public sealed class DependencyResolutionMessageTests : IDisposable
{
    private readonly CkCompileFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task UnsatisfiableRange_NamesVisibleVersionsAndTheOrderingRule()
    {
        await _fixture.CompileAndPublishAsync(_fixture.WriteSystemModel("2.4.0"));
        var dependent = _fixture.WriteSource("dep", "Dep-1.0.0", ["System-[2.5,3.0)"],
            new Dictionary<string, string>
            {
                ["types/thing.yaml"] = "types:\n  - typeId: Thing\n    derivedFromCkTypeId: ${System}/Entity\n"
            });

        var exception = await Assert.ThrowsAsync<ModelValidationException>(() => _fixture.CompileAsync(dependent));

        Assert.Contains("System-[2.5,3.0)", exception.Message);
        Assert.Contains("LocalFileSystemCatalog: 2.4.0", exception.Message);
        Assert.Contains("ProjectReference", exception.Message);
    }

    [Fact]
    public async Task UnknownModel_SaysNoCatalogKnowsIt()
    {
        var dependent = _fixture.WriteSource("dep", "Dep-1.0.0", ["Nowhere-[1.0,2.0)"],
            new Dictionary<string, string> { ["attributes/a.yaml"] = "attributes:\n  - id: A\n    valueType: String\n" });

        var exception = await Assert.ThrowsAsync<ModelValidationException>(() => _fixture.CompileAsync(dependent));

        Assert.Contains("no catalog knows any version of Nowhere", exception.Message);
    }
}
