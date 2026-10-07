namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     Review M9 (message 105): a Hidden attribute must not be reachable through display rules (feed
///     <c>rtDisplayName</c>), text indexes (feed search) or the owner path (also not MethodOnly).
/// </summary>
public sealed class RestrictedAttributeRuleTests : IDisposable
{
    private readonly CkCompileFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private async Task<Exception?> CompileAsync(string typeExtras, string secretAccess = "Hidden")
    {
        await _fixture.CompileAndPublishAsync(_fixture.WriteSystemModel("2.5.0"));
        var source = _fixture.WriteSource($"m9-{Guid.NewGuid():N}", "Secrets-1.0.0", ["System-[2.5,3.0)"],
            new Dictionary<string, string>
            {
                ["attributes/a.yaml"] =
                    "attributes:\n  - id: Code\n    valueType: String\n  - id: PasswordHash\n    valueType: String\n",
                ["types/t.yaml"] =
                    "types:\n  - typeId: Account\n    derivedFromCkTypeId: ${System}/Entity\n" + typeExtras +
                    "    attributes:\n" +
                    "      - id: ${this}/Code\n        name: Code\n        isOptional: true\n" +
                    "      - id: ${this}/PasswordHash\n        name: PasswordHash\n        isOptional: true\n" +
                    $"        access: {secretAccess}\n"
            }, ckLanguage: 2);
        try
        {
            await _fixture.CompileAsync(source);
            return null;
        }
        catch (Exception e)
        {
            return e;
        }
    }

    private static string Describe(Exception? e) =>
        e == null ? "" : e + string.Join(" ",
            (e as Contracts.CompilerException)?.OperationResult?.Messages.Select(m => m.ToString()) ?? []);

    [Theory]
    [InlineData("    displayNameRule: \"${PasswordHash}\"\n")]
    [InlineData("    displayDescriptionRule: \"${Code} ${PasswordHash}\"\n")]
    [InlineData("    ownerAttributePath: PasswordHash\n")]
    [InlineData("    indexes:\n      - indexType: Text\n        language: en\n        fields:\n          - weight: 1\n            attributePaths:\n              - PasswordHash\n")]
    public async Task HiddenAttribute_InDerivedRule_IsRejected(string typeExtras)
    {
        var exception = await CompileAsync(typeExtras);

        Assert.NotNull(exception);
        Assert.Contains("105", Describe(exception));
        Assert.Contains("PasswordHash", Describe(exception));
    }

    [Fact]
    public async Task MethodOnlyAttribute_AsOwnerPath_IsRejected()
    {
        var exception = await CompileAsync("    ownerAttributePath: PasswordHash\n", "MethodOnly");

        Assert.Contains("105", Describe(exception));
    }

    [Theory]
    [InlineData("    displayNameRule: \"${Code}\"\n")]
    [InlineData("    ownerAttributePath: Code\n")]
    public async Task VisibleAttribute_InDerivedRule_Compiles(string typeExtras)
    {
        Assert.Null(await CompileAsync(typeExtras));
    }
}
