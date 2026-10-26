using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     AB#7467, message 138 <c>ExportedRecordDropsRequiredMember</c> through the real compile pipeline (the code path
///     of <c>octo-ckc -c compile</c> and the <c>CkCompile</c> MSBuild task): the AB#7446 shape compiles with a
///     warning, never with an error.
/// </summary>
public sealed class CkExportedRecordMemberCompileTests : IDisposable
{
    private readonly CkCompileFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private async Task<OperationResult> CompileAsync(string externalIdOwnership)
    {
        await _fixture.CompileAndPublishAsync(_fixture.WriteSystemModel("2.5.0"));

        // The dependency: DataPoint with a required member ExternalId (System.Communication 4.10.0 shape).
        await _fixture.CompileAndPublishAsync(_fixture.WriteSource("dep", "Dep-1.0.0", ["System-[2.5,3.0)"],
            new Dictionary<string, string>
            {
                ["attributes/a.yaml"] = $"""
                    attributes:
                      - id: ExternalId
                        valueType: String
                        ownership: {externalIdOwnership}
                      - id: Value
                        valueType: String
                        ownership: RuntimeState
                    """,
                ["records/r.yaml"] = """
                    records:
                      - recordId: DataPoint
                        attributes:
                          - id: ${this}/ExternalId
                            name: ExternalId
                          - id: ${this}/Value
                            name: Value
                            isOptional: true
                    """
            }));

        var source = _fixture.WriteSource($"own-{Guid.NewGuid():N}", "Own-1.0.0", ["System-[2.5,3.0)", "Dep-[1.0,2.0)"],
            new Dictionary<string, string>
            {
                ["attributes/a.yaml"] = """
                    attributes:
                      - id: States
                        valueType: RecordArray
                        valueCkRecordId: ${Dep}/DataPoint
                    """,
                ["types/t.yaml"] = """
                    types:
                      - typeId: Control
                        derivedFromCkTypeId: ${System}/Entity
                        attributes:
                          - id: ${this}/States
                            name: States
                            isOptional: true
                    """
            });

        var result = new OperationResult();
        await _fixture.Services.GetRequiredService<Contracts.Services.ICompilerService>()
            .CompileInMemoryAsync(source, result);
        return result;
    }

    [Fact]
    public async Task Ab7446Shape_CompilesWithWarning138()
    {
        var result = await CompileAsync("RuntimeState");

        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Messages));
        var message = Assert.Single(result.Messages, m => m.MessageNumber == 138);
        Assert.Equal(MessageLevel.Warning, message.MessageLevel);
        Assert.Equal("States.ExternalId", message.Parameters["memberPath"]);
        Assert.Equal("States", message.Parameters["attributeName"]);
        Assert.Equal("RuntimeState", message.Parameters["memberOwnership"]);
    }

    [Fact]
    public async Task SeedOwnedMember_CompilesWithoutWarning()
    {
        var result = await CompileAsync("SeedOwned");

        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Messages));
        Assert.DoesNotContain(result.Messages, m => m.MessageNumber == 138);
    }
}
