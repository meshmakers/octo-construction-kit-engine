using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.BlueprintCatalogs;

/// <summary>
///     AB#5531 / AB#5528 decision 9: blueprint seeds carry only empty values or placeholders for
///     Secret attributes.
/// </summary>
public class BlueprintSeedSecretLintTests : IDisposable
{
    private readonly string _blueprintDirectory =
        Path.Combine(Path.GetTempPath(), $"bp-seed-secret-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_blueprintDirectory))
        {
            Directory.Delete(_blueprintDirectory, true);
        }

        GC.SuppressFinalize(this);
    }

    private sealed class FakeResolver(params string[] secretAttributeIds) : IBlueprintSecretAttributeResolver
    {
        public List<CkModelIdVersionRange> RequestedDependencies { get; } = [];

        public Task<BlueprintSecretAttributeResolution> ResolveAsync(IEnumerable<CkModelIdVersionRange> dependencies,
            CancellationToken cancellationToken = default)
        {
            RequestedDependencies.AddRange(dependencies);
            return Task.FromResult(new BlueprintSecretAttributeResolution(
                new HashSet<string>(secretAttributeIds, StringComparer.Ordinal), ["Unknown-[1.0,2.0)"]));
        }
    }

    private void WriteBlueprint(string secretValueYaml)
    {
        Directory.CreateDirectory(Path.Combine(_blueprintDirectory, "seed-data"));
        File.WriteAllText(Path.Combine(_blueprintDirectory, "blueprint.yaml"), """
            $schema: https://schemas.meshmakers.cloud/blueprint-meta.schema.json
            blueprintId: TestBlueprint-1.0.0
            seedDataPath: seed-data/entities.yaml
            ckModelDependencies:
              - System.Communication-[3.40,4.0)
            """);
        File.WriteAllText(Path.Combine(_blueprintDirectory, "seed-data", "entities.yaml"), $"""
            $schema: https://schemas.meshmakers.cloud/runtime-model.schema.json
            dependencies:
              - System.Communication-[3.40,4.0)
            entities:
              - rtId: aa0000000000000000000001
                ckTypeId: System.Communication/EMailSenderConfiguration
                attributes:
                  - id: System/Name
                    value: Mail
                  - id: System.Communication/Password
            {secretValueYaml}
                  - id: System.Communication/Overrides
                    value:
                      - ckRecordId: System.Communication/ValueOverride
                        attributes:
                          - id: System.Communication/Key
                            value: Token
                          - id: System.Communication/OverrideValue
                            value: ''
            """);
    }

    [Theory]
    [InlineData("        value: ''")]
    [InlineData("        value: '   '")]
    [InlineData("        value:")]
    [InlineData("        value: null")]
    [InlineData("")]
    public async Task ValidateAsync_EmptyOrOmitted_IsValid(string secretValueYaml)
    {
        WriteBlueprint(secretValueYaml);
        var resolver = new FakeResolver("System.Communication/Password-1", "System.Communication/OverrideValue-1");
        var compiler = new BlueprintCompilerService(new BlueprintYamlSerializer(),
            NullLogger<BlueprintCompilerService>.Instance, resolver);

        var operationResult = new OperationResult();
        await compiler.ValidateAsync(_blueprintDirectory, operationResult, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(operationResult.Messages, m => m.MessageLevel == MessageLevel.Error);
        // The unresolvable model is a warning, never an error.
        Assert.Contains(operationResult.Messages, m =>
            m is { MessageLevel: MessageLevel.Warning, MessageNumber: BlueprintSeedSecretLint.UnresolvedModelMessageNumber });
        Assert.Contains(resolver.RequestedDependencies, d => d.Name == "System.Communication");
    }

    [Fact]
    public async Task ValidateAsync_RealSecretValue_IsError_WithoutEchoingIt()
    {
        WriteBlueprint("        value: 'Hunter2!'");
        var compiler = new BlueprintCompilerService(new BlueprintYamlSerializer(),
            NullLogger<BlueprintCompilerService>.Instance, new FakeResolver("System.Communication/Password-1"));

        var operationResult = new OperationResult();
        await Assert.ThrowsAsync<BlueprintCatalogException>(() =>
            compiler.ValidateAsync(_blueprintDirectory, operationResult, TestContext.Current.CancellationToken));

        var error = Assert.Single(operationResult.Messages, m => m.MessageLevel == MessageLevel.Error);
        Assert.Equal(BlueprintSeedSecretLint.SecretSeedValueMessageNumber, error.MessageNumber);
        Assert.Contains("System.Communication/Password", error.MessageText);
        Assert.DoesNotContain("Hunter2", error.MessageText);
    }

    /// <summary>
    ///     Decisions 2026-10-06 item 1: the former placeholder forms are ordinary values - a seed that carries
    ///     one in a Secret slot fails the lint like any other value.
    /// </summary>
    [Theory]
    [InlineData("        value: '<SET_AFTER_INSTALL>'")]
    [InlineData("        value: TODO_SET_PASSWORD")]
    [InlineData("        value: 'TODO_SET_AZURE_TENANT_ID'")]
    public async Task ValidateAsync_Placeholder_IsError(string secretValueYaml)
    {
        WriteBlueprint(secretValueYaml);
        var compiler = new BlueprintCompilerService(new BlueprintYamlSerializer(),
            NullLogger<BlueprintCompilerService>.Instance, new FakeResolver("System.Communication/Password-1"));

        var operationResult = new OperationResult();
        await Assert.ThrowsAsync<BlueprintCatalogException>(() =>
            compiler.ValidateAsync(_blueprintDirectory, operationResult, TestContext.Current.CancellationToken));

        var error = Assert.Single(operationResult.Messages, m => m.MessageLevel == MessageLevel.Error);
        Assert.Equal(BlueprintSeedSecretLint.SecretSeedValueMessageNumber, error.MessageNumber);
        Assert.Contains("TODO_SET_<NAME>", error.MessageText);
        Assert.DoesNotContain("SET_AFTER_INSTALL", error.MessageText);
        Assert.DoesNotContain("TODO_SET_PASSWORD", error.MessageText);
        Assert.DoesNotContain("AZURE_TENANT_ID", error.MessageText);
    }

    [Fact]
    public async Task ValidateAsync_SecretInsideRecordArray_IsError()
    {
        WriteBlueprint("        value: ''");
        // Turn the record's OverrideValue into a real value.
        var seedPath = Path.Combine(_blueprintDirectory, "seed-data", "entities.yaml");
        File.WriteAllText(seedPath, System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(seedPath),
            @"(OverrideValue\s*\n\s*value: )''", "${1}abc123"));
        var compiler = new BlueprintCompilerService(new BlueprintYamlSerializer(),
            NullLogger<BlueprintCompilerService>.Instance,
            new FakeResolver("System.Communication/Password-1", "System.Communication/OverrideValue-1"));

        var operationResult = new OperationResult();
        await Assert.ThrowsAsync<BlueprintCatalogException>(() =>
            compiler.ValidateAsync(_blueprintDirectory, operationResult, TestContext.Current.CancellationToken));

        var error = Assert.Single(operationResult.Messages, m => m.MessageLevel == MessageLevel.Error);
        Assert.Contains("OverrideValue", error.MessageText);
    }

    [Fact]
    public async Task ValidateAsync_WithoutResolver_SkipsTheLint()
    {
        WriteBlueprint("        value: 'Hunter2!'");
        var compiler = new BlueprintCompilerService(new BlueprintYamlSerializer(),
            NullLogger<BlueprintCompilerService>.Instance);

        var operationResult = new OperationResult();
        await compiler.ValidateAsync(_blueprintDirectory, operationResult, TestContext.Current.CancellationToken);

        Assert.Empty(operationResult.Messages);
    }

    [Theory]
    [InlineData("System.Communication/Password", "System.Communication/Password-1")]
    [InlineData("System.Communication-3.40.0/Password", "System.Communication/Password-1")]
    [InlineData("Query/Filter-2", "Query/Filter-2")]
    [InlineData("NoModel", null)]
    [InlineData("", null)]
    public void NormaliseAttributeId(string input, string? expected)
    {
        Assert.Equal(expected, BlueprintSeedSecretLint.NormaliseAttributeId(input));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData(null, true)]
    [InlineData("<SET_AFTER_INSTALL>", false)]
    [InlineData(" <x> ", false)]
    [InlineData("<>", false)]
    [InlineData("<a><b>", false)]
    [InlineData("pass<word>", false)]
    [InlineData("Hunter2", false)]
    [InlineData("TODO_SET_PASSWORD", false)]
    [InlineData(" TODO_SET_CLIENT_SECRET ", false)]
    [InlineData("TODO_SET_", false)]
    [InlineData("TODO_SET_password", false)]
    public void AllowedSeedValues(string? value, bool allowed)
    {
        Assert.Equal(allowed, SecretAttributeConventions.IsAllowedSeedValue(value));
    }

    /// <summary>
    ///     Both legacy placeholder forms - <c>&lt;...&gt;</c> and <c>TODO_SET_&lt;UPPER_SNAKE&gt;</c> - are
    ///     recognised by the migration-only check (decisions 2026-10-06 item 1).
    /// </summary>
    [Theory]
    [InlineData("<SET_AFTER_INSTALL>", true)]
    [InlineData("<set-after-install>", true)]
    [InlineData("TODO_SET_PASSWORD", true)]
    [InlineData("TODO_SET_AZURE_TENANT_ID", true)]
    [InlineData("TODO_SET_K1", true)]
    [InlineData("  TODO_SET_EMAIL_ADDRESS\t", true)]
    [InlineData("TODO_SET_", false)]
    [InlineData("TODO_SET__PASSWORD", false)]
    [InlineData("TODO_SET_PASSWORD_", false)]
    [InlineData("TODO_SET_PASS WORD", false)]
    [InlineData("TODO_SET_Password", false)]
    [InlineData("todo_set_password", false)]
    [InlineData("XTODO_SET_PASSWORD", false)]
    [InlineData("TODO_SET_PASSWORD!", false)]
    [InlineData("<>", false)]
    [InlineData("<<x>>", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("Hunter2", false)]
    public void IsLegacyPlaceholder_RecognisesBothForms(string? value, bool expected)
    {
        Assert.Equal(expected, SecretAttributeConventions.IsLegacyPlaceholder(value));
    }

    [Fact]
    public void IsLegacyPlaceholder_Forms_AreDistinguishable()
    {
        Assert.True(SecretAttributeConventions.IsAngleBracketPlaceholder("<X>"));
        Assert.False(SecretAttributeConventions.IsTodoSetPlaceholder("<X>"));
        Assert.True(SecretAttributeConventions.IsTodoSetPlaceholder("TODO_SET_X"));
        Assert.False(SecretAttributeConventions.IsAngleBracketPlaceholder("TODO_SET_X"));
    }
}
