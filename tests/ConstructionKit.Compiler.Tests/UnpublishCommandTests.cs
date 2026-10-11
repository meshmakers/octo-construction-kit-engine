using FakeItEasy;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     AB#5433: <c>octo-ckc -c Unpublish</c> executed in-process through the real command parser against a
///     <c>LocalFileSystemCatalog</c> in a temp directory (a scratch catalog; no shared catalog is ever touched).
/// </summary>
public sealed class UnpublishCommandTests : IDisposable
{
    private readonly string _root;
    private readonly string _sourceDir;
    private readonly string _catalogDir;
    private readonly string _reportPath;
    private readonly IEnvironmentService _environment;
    private readonly ServiceProvider _serviceProvider;

    public UnpublishCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"UnpublishCmdTest_{Guid.NewGuid():N}");
        _sourceDir = Path.Combine(_root, "src");
        _catalogDir = Path.Combine(_root, "catalog");
        _reportPath = Path.Combine(_root, "report.md");
        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_catalogDir);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddConstructionKit();
        services.Configure<LocalFileSystemCatalogOptions>(options =>
        {
            options.ApplyRootPath(_catalogDir);
            options.IsEnabled = true;
        });
        services.Configure<PublicGitHubCatalogOptions>(options => options.IsEnabled = false);
        services.Configure<PrivateGitHubCatalogOptions>(options => options.IsEnabled = false);

        _environment = A.Fake<IEnvironmentService>();
        services.AddSingleton(_environment);
        services.AddSingleton(A.Fake<IConsoleService>());
        services.AddSingleton<IParserService, ParserService>();
        services.AddSingleton<ICommandParser, CommandParser>();
        services.AddTransient<ICommand, UnpublishCommand>();
        services.AddTransient<ICommand, ValidateVersionCommand>();

        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private void WriteSource(string version, string? dependency = null)
    {
        Directory.CreateDirectory(Path.Combine(_sourceDir, "attributes"));
        var dependencies = dependency == null ? "" : $"dependencies:\n  - {dependency}\n";
        File.WriteAllText(Path.Combine(_sourceDir, "ckModel.yaml"),
            "\"$schema\": \"https://schemas.meshmakers.cloud/construction-kit-meta.schema.json\"\n" +
            dependencies + $"modelId: CmdFixture-{version}\n");
        File.WriteAllText(Path.Combine(_sourceDir, "attributes", "serial.yaml"),
            "\"$schema\": \"https://schemas.meshmakers.cloud/construction-kit-elements.schema.json\"\n" +
            "attributes:\n  - id: Serial\n    valueType: String\n    isRuntimeState: false\n");
    }

    private async Task PublishAsync(string version, string? description = null)
    {
        WriteSource(version);
        var operationResult = new OperationResult();
        var compiled = await _serviceProvider.GetRequiredService<ICompilerService>()
            .CompileInMemoryAsync(_sourceDir, operationResult);
        Assert.False(operationResult.HasErrors);
        if (description != null)
        {
            compiled.Description = description;
        }

        await _serviceProvider.GetRequiredService<ICatalogService>().PublishAsync(LocalFileSystemCatalog.Name,
            compiled, new OriginFileResolver(_sourceDir), isForced: false);
    }

    private Task RunAsync(string command, params string[] arguments)
    {
        A.CallTo(() => _environment.GetCommandLineArgs())
            .Returns(new[] { "octo-ckc", "-c", command }.Concat(arguments).ToArray());
        return _serviceProvider.GetRequiredService<ICommandParser>().ParseAndValidateAsync();
    }

    private Dictionary<string, string> Snapshot() =>
        Directory.GetFiles(_catalogDir, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}cache{Path.DirectorySeparatorChar}") &&
                        !f.EndsWith(".lock"))
            .ToDictionary(f => Path.GetRelativePath(_catalogDir, f), File.ReadAllText);

    [Fact]
    public async Task DryRun_ChangesNothing_AndIsTheDefault()
    {
        await PublishAsync("2.2.2");
        await PublishAsync("2.3.0");
        var before = Snapshot();

        await RunAsync("Unpublish", "-m", "CmdFixture", "-ve", "2.3.0");

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Force_WithdrawsTheVersion_AndTheNextValidateVersionUsesTheOlderBaseline()
    {
        // The 2026-09-30 replay: 2.3.0 was published prematurely, nothing depends on it.
        await PublishAsync("2.2.2");
        await PublishAsync("2.3.0");

        await RunAsync("Unpublish", "-m", "CmdFixture", "-ve", "2.3.0", "-f", "-rs", "premature publish");

        Assert.False(await _serviceProvider.GetRequiredService<ICatalogService>()
            .IsExistingAsync(new CkModelId("CmdFixture", "2.3.0")));
        Assert.True(await _serviceProvider.GetRequiredService<ICatalogService>()
            .IsExistingAsync(new CkModelId("CmdFixture", "2.2.2")));

        WriteSource("2.2.3");
        await RunAsync("ValidateVersion", "-p", _sourceDir, "-rf", "-o", _reportPath);
        var report = await File.ReadAllTextAsync(_reportPath, TestContext.Current.CancellationToken);
        Assert.Contains("- Published: `2.2.2`", report);
        Assert.DoesNotContain("`2.3.0`", report.Replace("- Declared: `2.2.3`", ""));
    }

    [Fact]
    public async Task Force_OfAnUnknownVersion_FailsWithANonZeroExit()
    {
        await PublishAsync("1.0.0");

        var exception = await Assert.ThrowsAsync<ModelValidationException>(() =>
            RunAsync("Unpublish", "-m", "CmdFixture", "-ve", "9.9.9", "-f"));

        Assert.Contains("CmdFixture-9.9.9", exception.Message);
    }

    [Fact]
    public async Task Force_WithADependent_IsRefused_AndNamesIt()
    {
        await PublishAsync("1.0.0");
        await PublishAsync("1.1.0");
        var dependentDir = Path.Combine(_root, "dependent");
        Directory.CreateDirectory(dependentDir);
        File.WriteAllText(Path.Combine(dependentDir, "ckModel.yaml"),
            "\"$schema\": \"https://schemas.meshmakers.cloud/construction-kit-meta.schema.json\"\n" +
            "dependencies:\n  - CmdFixture-[1.1,2.0)\nmodelId: CmdDependent-1.0.0\n");
        var operationResult = new OperationResult();
        var compiled = await _serviceProvider.GetRequiredService<ICompilerService>()
            .CompileInMemoryAsync(dependentDir, operationResult);
        Assert.False(operationResult.HasErrors);
        await _serviceProvider.GetRequiredService<ICatalogService>().PublishAsync(LocalFileSystemCatalog.Name,
            compiled, new OriginFileResolver(dependentDir), isForced: false);
        var before = Snapshot();

        var exception = await Assert.ThrowsAsync<ModelValidationException>(() =>
            RunAsync("Unpublish", "-m", "CmdFixture", "-ve", "1.1.0", "-f"));

        Assert.Contains("CmdDependent-1.0.0", exception.Message);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Force_RerunIsIdempotent_AndRepublishingTheBurnedNumberIsRefused()
    {
        await PublishAsync("1.0.0");
        await PublishAsync("1.1.0");
        await RunAsync("Unpublish", "-m", "CmdFixture", "-ve", "1.1.0", "-f");
        var after = Snapshot();

        await RunAsync("Unpublish", "-m", "CmdFixture", "-ve", "1.1.0", "-f");
        Assert.Equal(after, Snapshot());

        var exception = await Assert.ThrowsAsync<ModelCatalogException>(() => PublishAsync("1.1.0", "different"));
        Assert.Contains("burned", exception.Message);
        await PublishAsync("1.1.1");
    }

    [Fact]
    public async Task InvalidVersion_IsRejectedBeforeAnythingIsRead()
    {
        var exception = await Assert.ThrowsAsync<ModelValidationException>(() =>
            RunAsync("Unpublish", "-m", "CmdFixture", "-ve", "not-a-version", "-f"));

        Assert.Contains("Invalid version", exception.Message);
    }
}
