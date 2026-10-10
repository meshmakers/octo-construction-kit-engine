using System.Security.Cryptography;
using FakeItEasy;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     AB#5437: <c>octo-ckc -c ValidateCascade</c> executed in-process through the real command parser against a
///     local catalog in a temp directory (no network): exit code, Markdown report and read-only behaviour.
/// </summary>
public sealed class ValidateCascadeCommandTests : IDisposable
{
    private readonly IEnvironmentService _environment = A.Fake<IEnvironmentService>();
    private readonly CkCompileFixture _fixture;

    public ValidateCascadeCommandTests()
    {
        _fixture = new CkCompileFixture(services =>
        {
            services.AddSingleton(_environment);
            services.AddSingleton(A.Fake<IConsoleService>());
            services.AddSingleton<IParserService, ParserService>();
            services.AddSingleton<ICommandParser, CommandParser>();
            services.AddTransient<ICommand, ValidateCascadeCommand>();
        });
    }

    public void Dispose() => _fixture.Dispose();

    private string WriteSystem(string version, bool withDescription)
    {
        var attributes = "attributes:\n  - id: Name\n    valueType: String\n" +
                         (withDescription ? "  - id: Description\n    valueType: String\n" : "");
        return _fixture.WriteSource($"system-{version}", $"System-{version}", null, new Dictionary<string, string>
        {
            ["attributes/attributes.yaml"] = attributes,
            ["types/entity.yaml"] = "types:\n  - typeId: Entity\n    isAbstract: true\n    attributes:\n" +
                                    "      - id: ${this}/Name\n        name: Name\n        isOptional: true\n"
        });
    }

    private async Task PublishWorldAsync()
    {
        await _fixture.CompileAndPublishAsync(WriteSystem("2.2.0", withDescription: true));
        await _fixture.CompileAndPublishAsync(_fixture.WriteSource("plant", "Plant-1.0.0", ["System-[2.2,3.0)"],
            new Dictionary<string, string>
            {
                ["types/machine.yaml"] = "types:\n  - typeId: Machine\n    derivedFromCkTypeId: ${System}/Entity\n" +
                                         "    attributes:\n      - id: ${System}/Description\n        name: Description\n" +
                                         "        isOptional: true\n"
            }));
    }

    private Task RunAsync(params string[] arguments)
    {
        A.CallTo(() => _environment.GetCommandLineArgs())
            .Returns(new[] { "octo-ckc", "-c", "ValidateCascade" }.Concat(arguments).ToArray());
        return _fixture.Services.GetRequiredService<ICommandParser>().ParseAndValidateAsync();
    }

    private static string Snapshot(string directory) =>
        string.Join("|", Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).OrderBy(f => f)
            .Select(f => $"{Path.GetRelativePath(directory, f)}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))}"));

    [Fact]
    public async Task A_breaking_candidate_fails_the_command_and_the_report_lists_every_dependent()
    {
        await PublishWorldAsync();
        var candidate = WriteSystem("2.3.0", withDescription: false);
        var report = Path.Combine(_fixture.Root, "cascade.md");
        var before = Snapshot(_fixture.CatalogDir);

        var exception = await Assert.ThrowsAsync<ModelValidationException>(
            () => RunAsync("-p", candidate, "-o", report));

        Assert.Contains("System-2.3.0 would break 1 dependent(s): Plant-1.0.0", exception.Message);
        var markdown = await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken);
        Assert.Contains("`Plant-1.0.0`", markdown);
        Assert.Contains("**Breaks**", markdown);
        Assert.Contains("Description-1", markdown);
        // Read-only: no catalog write, the candidate is not registered.
        Assert.Equal(before, Snapshot(_fixture.CatalogDir));
    }

    [Fact]
    public async Task A_compatible_candidate_exits_cleanly()
    {
        await PublishWorldAsync();
        var candidate = WriteSystem("2.3.0", withDescription: true);
        var report = Path.Combine(_fixture.Root, "cascade.md");
        var before = Snapshot(_fixture.CatalogDir);

        await RunAsync("-p", candidate, "-o", report);

        var markdown = await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken);
        Assert.Contains("1 dependent(s): 0 Breaks", markdown);
        Assert.Contains("`Plant-1.0.0`", markdown);
        Assert.Equal(before, Snapshot(_fixture.CatalogDir));
    }

    [Fact]
    public async Task The_local_catalog_options_apply_to_the_invocation_only()
    {
        await PublishWorldAsync();
        var emptyCatalog = Path.Combine(_fixture.Root, "empty-catalog");
        Directory.CreateDirectory(emptyCatalog);
        var candidate = WriteSystem("2.3.0", withDescription: false);

        // With an empty local catalog there are no dependents (and no baseline): nothing breaks.
        await RunAsync("-p", candidate, "-lcr", emptyCatalog);

        Assert.Equal(emptyCatalog, _fixture.Services.GetRequiredService<IOptions<Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs.LocalFileSystemCatalogOptions>>().Value.RootPath);
    }
}
