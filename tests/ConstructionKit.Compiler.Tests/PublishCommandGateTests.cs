using System.Security.Cryptography;
using FakeItEasy;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     AB#6386: <c>octo-ckc -c Publish</c> executed in-process through the real command parser against an in-memory
///     published catalog (no network, no shared catalog). The refusal comes from the catalog service, so the MSBuild
///     publish of <c>CkCompile</c> (same call) is covered by the same implementation.
/// </summary>
public sealed class PublishCommandGateTests : IDisposable
{
    private readonly IEnvironmentService _environment = A.Fake<IEnvironmentService>();
    private readonly InMemoryPublishedCatalog _published = new();
    private readonly CkCompileFixture _fixture;
    private int _run;

    public PublishCommandGateTests()
    {
        _fixture = new CkCompileFixture(services =>
        {
            services.AddSingleton<ICatalog>(_published);
            services.AddSingleton(_environment);
            services.AddSingleton(A.Fake<IConsoleService>());
            services.AddSingleton<IParserService, ParserService>();
            services.AddSingleton<ICommandParser, CommandParser>();
            services.AddTransient<ICommand, PublishCommand>();
        });
    }

    public void Dispose() => _fixture.Dispose();

    private string WriteModel(string version, string enumValues = "A,B")
    {
        var values = string.Concat(enumValues.Split(',').Select((v, i) =>
            $"          - key: {i}\n            name: {v}\n"));
        return _fixture.WriteSource("publish", $"PublishModel-{version}", null, new Dictionary<string, string>
        {
            ["attributes/attributes.yaml"] = "attributes:\n  - id: Serial\n    valueType: String\n",
            ["enums/state.yaml"] = "enums:\n  - enumId: State\n    values:\n" + values
        }, 2);
    }

    /// <summary>Compiles the source to a JSON file, exactly as the build does before <c>octo-ckc -c Publish</c>.</summary>
    private async Task<string> CompileToFileAsync(string sourceDir)
    {
        var operationResult = new OperationResult();
        var output = Path.Combine(_fixture.Root, "out", $"run{_run++}");
        var result = await _fixture.Services.GetRequiredService<ICompilerService>()
            .CompileAsync(sourceDir, output, null, operationResult);
        Assert.False(operationResult.HasErrors);
        return result.CompiledModelFile;
    }

    private async Task SeedAsync(string version) =>
        await _published.PublishAsync(await _fixture.CompileAsync(WriteModel(version)));

    private Task PublishAsync(string compiledFile, bool replace = false)
    {
        var arguments = new List<string> { "octo-ckc", "-c", "Publish", "-f", compiledFile, "-c", InMemoryPublishedCatalog.Name };
        if (replace)
        {
            arguments.Add("-r");
        }

        A.CallTo(() => _environment.GetCommandLineArgs()).Returns(arguments.ToArray());
        return _fixture.Services.GetRequiredService<ICommandParser>().ParseAndValidateAsync();
    }

    private async Task<string> HashesAsync() =>
        string.Join("|", (await CatalogContentAsync()).Select(x => $"{x.Key}:{x.Value}"));

    private async Task<IReadOnlyList<KeyValuePair<string, string>>> CatalogContentAsync()
    {
        var list = new List<KeyValuePair<string, string>>();
        await foreach (var item in _published.ListAsync(null))
        {
            var model = await _published.GetAsync(item.ModelId, new OperationResult());
            await using var stream = new MemoryStream();
            await _fixture.Services.GetRequiredService<Contracts.Serialization.ICkJsonSerializer>()
                .SerializeAsync(new StreamWriter(stream) { AutoFlush = true }, model);
            list.Add(new(item.ModelId.FullName, Convert.ToHexString(SHA256.HashData(stream.ToArray()))));
        }

        return list.OrderBy(x => x.Key, StringComparer.Ordinal).ToList();
    }

    [Fact]
    public async Task A_breaking_version_is_refused_with_a_non_zero_failure_and_the_catalog_is_unchanged()
    {
        await SeedAsync("2.4.0");
        var before = await HashesAsync();
        var file = await CompileToFileAsync(WriteModel("2.5.0", "A"));

        var exception = await Assert.ThrowsAsync<CompilerException>(() => PublishAsync(file));

        Assert.Contains("OCTO-CK200", exception.Message);
        Assert.Contains("3.0.0", exception.Message);
        Assert.Equal(before, await HashesAsync());
        Assert.Contains(exception.OperationResult.Messages, m => m.MessageNumber == 200);
    }

    [Fact]
    public async Task The_replace_switch_does_not_skip_the_gate()
    {
        await SeedAsync("2.4.0");
        var file = await CompileToFileAsync(WriteModel("2.5.0", "A"));

        var exception = await Assert.ThrowsAsync<CompilerException>(() => PublishAsync(file, replace: true));

        Assert.Contains("OCTO-CK200", exception.Message);
    }

    [Fact]
    public async Task The_same_change_with_a_major_bump_is_published()
    {
        await SeedAsync("2.4.0");

        await PublishAsync(await CompileToFileAsync(WriteModel("3.0.0", "A")));

        Assert.True(await _published.IsExistingAsync(new CkModelId("PublishModel", "3.0.0")));
    }

    [Fact]
    public async Task A_hand_edited_compiled_file_gets_the_verdict_of_its_content()
    {
        await SeedAsync("2.4.0");
        var file = await CompileToFileAsync(WriteModel("3.0.0", "A"));
        // Lower the declared version in the compiled file by hand: the removed enum value still needs a major.
        var edited = Path.Combine(_fixture.Root, "edited.json");
        var json = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
        Assert.Contains("3.0.0", json);
        await File.WriteAllTextAsync(edited, json.Replace("3.0.0", "2.4.1"), TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<CompilerException>(() => PublishAsync(edited));

        Assert.Contains("OCTO-CK200", exception.Message);
        Assert.False(await _published.IsExistingAsync(new CkModelId("PublishModel", "2.4.1")));
    }
}
