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
                            value: '<TOKEN>'
            """);
    }

    [Theory]
    [InlineData("        value: '<SET_AFTER_INSTALL>'")]
    [InlineData("        value: ''")]
    [InlineData("        value:")]
    [InlineData("        value: null")]
    [InlineData("")]
    public async Task ValidateAsync_PlaceholderOrEmpty_IsValid(string secretValueYaml)
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

    [Fact]
    public async Task ValidateAsync_SecretInsideRecordArray_IsError()
    {
        WriteBlueprint("        value: '<PW>'");
        // Turn the record's OverrideValue into a real value.
        var seedPath = Path.Combine(_blueprintDirectory, "seed-data", "entities.yaml");
        File.WriteAllText(seedPath, File.ReadAllText(seedPath).Replace("'<TOKEN>'", "abc123"));
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
    [InlineData("<SET_AFTER_INSTALL>", true)]
    [InlineData(" <x> ", true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData(null, true)]
    [InlineData("<>", false)]
    [InlineData("<a><b>", false)]
    [InlineData("pass<word>", false)]
    [InlineData("Hunter2", false)]
    public void AllowedSeedValues(string? value, bool allowed)
    {
        Assert.Equal(allowed, SecretAttributeConventions.IsAllowedSeedValue(value));
    }
}
