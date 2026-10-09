namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     Review M9 (message 105): a Hidden attribute must not be reachable through display rules (feed
///     <c>rtDisplayName</c>), text indexes (feed search) or the owner path (also not MethodOnly).
/// </summary>
public sealed class RestrictedAttributeRuleTests : IDisposable
{
    private readonly CkCompileFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private async Task<Exception?> CompileAsync(string typeExtras, string secretAccess = "Hidden",
        int? ckLanguage = 2, string attributeExtras = "", Dictionary<string, string>? extraFiles = null)
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
                    (secretAccess == "" ? "" : $"        access: {secretAccess}\n") + attributeExtras
            }.Concat(extraFiles ?? []).ToDictionary(k => k.Key, v => v.Value), ckLanguage: ckLanguage);
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

    // ── F1.2-S2 (AB#5911): Hidden parity with Secret (review N5/N6) ─────────────────────────────────

    private static string Index(string indexType, string path) =>
        $"    indexes:\n      - indexType: {indexType}\n        fields:\n          - attributePaths:\n              - {path}\n";

    [Theory]
    [InlineData("Unique")]
    [InlineData("UniqueNotDeleted")]
    [InlineData("Ascending")]
    public async Task HiddenAttribute_InAnyIndex_Is106(string indexType)
    {
        var description = Describe(await CompileAsync(Index(indexType, "PasswordHash")));

        Assert.Contains("106", description);
        Assert.Contains(indexType, description);
    }

    [Fact]
    public async Task LowerCaseTextIndexPath_ReachesTheHiddenAttribute_105()
    {
        var description = Describe(await CompileAsync(
            "    indexes:\n      - indexType: Text\n        language: en\n        fields:\n          - weight: 1\n            attributePaths:\n              - passwordHash\n"));

        Assert.Contains("105", description);
        Assert.Contains("passwordHash", description);
    }

    [Fact]
    public async Task UnknownIndexPath_Is109_InACkV2Model()
    {
        var description = Describe(await CompileAsync(Index("Ascending", "Nope")));

        Assert.Contains("109", description);
        Assert.Contains("Nope", description);
    }

    [Theory]
    [InlineData("code")] // case-insensitive, like the database
    [InlineData("RtWellKnownName")] // entity system field
    public async Task KnownOrSystemIndexPath_Compiles(string path)
    {
        Assert.Null(await CompileAsync(Index("Ascending", path)));
    }

    [Fact]
    public async Task UnknownIndexPath_InAV1Model_StillCompiles()
    {
        // No behaviour change for ckLanguage 1 (no access key either).
        Assert.Null(await CompileAsync(Index("Ascending", "Nope"), secretAccess: "", ckLanguage: null));
    }

    [Fact]
    public async Task HiddenAttribute_WithAutoCompleteValues_Is107()
    {
        var description = Describe(await CompileAsync("", attributeExtras: "        autoCompleteValues: [ a, b ]\n"));

        Assert.Contains("107", description);
        Assert.Contains("PasswordHash", description);
    }

    [Fact]
    public async Task HiddenAssociationRoleAttribute_Is108_ReadOnlyCompiles()
    {
        Dictionary<string, string> Role(string access) => new()
        {
            ["associations/roles.yaml"] =
                "associationRoles:\n  - id: Owns\n    inboundName: OwnedBy\n    outboundName: Owns\n" +
                "    inboundMultiplicity: N\n    outboundMultiplicity: N\n    attributes:\n" +
                $"      - id: ${{this}}/Code\n        name: Code\n        access: {access}\n"
        };

        var description = Describe(await CompileAsync("", secretAccess: "", extraFiles: Role("Hidden")));
        Assert.Contains("108", description);
        Assert.Null(await CompileAsync("", secretAccess: "", extraFiles: Role("ReadOnly")));
    }

    // ── Review G3 E-M1: sibling types share the collection's document shape ────────────────────────────────

    [Theory]
    [InlineData("Text", "105")]
    [InlineData("Ascending", "106")]
    public async Task IndexOnASibling_ReachingAHiddenAssignment_IsRejected(string indexType, string code)
    {
        await _fixture.CompileAndPublishAsync(_fixture.WriteSystemModel("2.5.0"));
        var index = indexType == "Text"
            ? "    indexes:\n      - indexType: Text\n        language: en\n        fields:\n          - weight: 1\n            attributePaths:\n              - Code\n"
            : "    indexes:\n      - indexType: Ascending\n        fields:\n          - attributePaths:\n              - Code\n";
        var source = _fixture.WriteSource($"sib-{Guid.NewGuid():N}", "Siblings-1.0.0", ["System-[2.5,3.0)"],
            new Dictionary<string, string>
            {
                ["attributes/a.yaml"] = "attributes:\n  - id: Code\n    valueType: String\n",
                ["types/t.yaml"] =
                    "types:\n  - typeId: Base\n    derivedFromCkTypeId: ${System}/Entity\n    isAbstract: true\n" +
                    "    derivable: Any\n" +
                    "  - typeId: Indexed\n    derivedFromCkTypeId: ${this}/Base\n" + index +
                    "    attributes:\n      - id: ${this}/Code\n        name: Code\n        isOptional: true\n" +
                    "  - typeId: Secretive\n    derivedFromCkTypeId: ${this}/Base\n" +
                    "    attributes:\n      - id: ${this}/Code\n        name: Code\n        isOptional: true\n        access: Hidden\n"
            }, ckLanguage: 2);

        Exception? exception = null;
        try
        {
            await _fixture.CompileAsync(source);
        }
        catch (Exception e)
        {
            exception = e;
        }

        var description = Describe(exception);
        Assert.Contains(code, description);
        Assert.Contains("Secretive", description);
    }
}
