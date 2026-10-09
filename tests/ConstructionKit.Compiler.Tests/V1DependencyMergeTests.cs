using Meshmakers.Octo.ConstructionKit.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     Review G3 E-H1: with range retention off a v1 compile behaves exactly like main. A library compiled against
///     System-2.5.0 carries the exact pin <c>System-[2.5.0]</c>; when the catalog already has System 2.6.0, the pin
///     merges into the consumer's declared <c>System-[2.5,3.0)</c> by overlap and resolves to 2.6.0, so the library's
///     reference <c>System-2.5.0/Entity</c> is unknown — the main behaviour (verified against main 0db1c50), not the
///     "multiple versions" error 66 the structural match produced.
/// </summary>
public sealed class V1DependencyMergeTests : IDisposable
{
    private readonly CkCompileFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task LibraryPinnedToAnOlderSystemMinor_StillCompiles_AgainstTheHighestSystem()
    {
        await _fixture.CompileAndPublishAsync(_fixture.WriteSystemModel("2.5.0"));
        var basic = await _fixture.CompileAndPublishAsync(_fixture.WriteSource("basic", "Basic-1.0.0",
            ["System-[2.5,3.0)"], new Dictionary<string, string>
            {
                ["types/thing.yaml"] = "types:\n  - typeId: Thing\n    derivedFromCkTypeId: ${System}/Entity\n"
            }));
        Assert.Equal("System-2.5.0", Assert.Single(basic.Dependencies!).FullName);
        await _fixture.CompileAndPublishAsync(_fixture.WriteSystemModel("2.6.0", withExtraAttribute: true));

        var operationResult = new OperationResult();
        var exception = await Record.ExceptionAsync(() => _fixture.Services
            .GetRequiredService<Contracts.Services.ICompilerService>()
            .CompileInMemoryAsync(_fixture.WriteSource("consumer", "Consumer-1.0.0",
                ["System-[2.5,3.0)", "Basic-[1.0,2.0)"], new Dictionary<string, string>
                {
                    ["types/gadget.yaml"] = "types:\n  - typeId: Gadget\n    derivedFromCkTypeId: ${Basic}/Thing\n"
                }), operationResult));

        Assert.NotNull(exception);
        Assert.Contains("System-2.5.0/Entity", exception.Message);
        Assert.DoesNotContain(operationResult.Messages, m => m.MessageNumber == 66);
    }
}
