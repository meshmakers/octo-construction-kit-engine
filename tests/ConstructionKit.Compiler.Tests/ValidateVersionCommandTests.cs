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
///     Command-level tests for <see cref="ValidateVersionCommand" />: the command is executed
///     in-process through the real command parser (arguments injected via a faked
///     <see cref="IEnvironmentService" />) against real engine services and a
///     <c>LocalFileSystemCatalog</c> in a temp directory — no network, no fakes below the
///     command boundary. Covers the error paths that have no engine-level test surface:
///     OCTO-CK103, the unknown-catalog refresh failure, migration reconciliation
///     skip/escalation and the changelog write gating.
/// </summary>
public sealed class ValidateVersionCommandTests : IDisposable
{
    private readonly string _root;
    private readonly string _sourceDir;
    private readonly string _catalogDir;
    private readonly string _reportPath;
    private readonly IEnvironmentService _environment;
    private readonly ServiceProvider _serviceProvider;

    public ValidateVersionCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"ValidateVersionCmdTest_{Guid.NewGuid():N}");
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

    /// <summary>
    ///     Writes a minimal, self-contained CK source (one attribute, one enum, no types) so
    ///     compilation needs no catalog content besides the model itself.
    /// </summary>
    private void WriteSource(string version, bool removeEnumValue = false, string? dependency = null)
    {
        Directory.CreateDirectory(Path.Combine(_sourceDir, "attributes"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "enums"));

        var dependencies = dependency == null ? "" : $"dependencies:\n  - {dependency}\n";
        File.WriteAllText(Path.Combine(_sourceDir, "ckModel.yaml"),
            "\"$schema\": \"https://schemas.meshmakers.cloud/construction-kit-meta.schema.json\"\n" +
            dependencies +
            $"modelId: CmdFixture-{version}\n");

        File.WriteAllText(Path.Combine(_sourceDir, "attributes", "serial.yaml"),
            "\"$schema\": \"https://schemas.meshmakers.cloud/construction-kit-elements.schema.json\"\n" +
            "attributes:\n  - id: Serial\n    valueType: String\n    isRuntimeState: false\n");

        var values = "      - key: 0\n        name: IdleState\n" +
                     (removeEnumValue ? "" : "      - key: 1\n        name: RunState\n");
        File.WriteAllText(Path.Combine(_sourceDir, "enums", "state.yaml"),
            "\"$schema\": \"https://schemas.meshmakers.cloud/construction-kit-elements.schema.json\"\n" +
            "enums:\n  - enumId: State\n    values:\n" + values);
    }

    /// <summary>
    ///     Publishes the current source as the baseline into the temp local catalog.
    /// </summary>
    private async Task PublishBaselineAsync()
    {
        var operationResult = new OperationResult();
        var compiled = await _serviceProvider.GetRequiredService<ICompilerService>()
            .CompileInMemoryAsync(_sourceDir, operationResult);
        Assert.False(operationResult.HasErrors);

        await _serviceProvider.GetRequiredService<ICatalogService>().PublishAsync(
            LocalFileSystemCatalog.Name, compiled, new OriginFileResolver(_sourceDir), isForced: true);
    }

    private Task RunAsync(params string[] arguments)
    {
        A.CallTo(() => _environment.GetCommandLineArgs())
            .Returns(new[] { "octo-ckc", "-c", "ValidateVersion" }.Concat(arguments).ToArray());
        return _serviceProvider.GetRequiredService<ICommandParser>().ParseAndValidateAsync();
    }

    [Fact]
    public async Task UnsatisfiableDependencyRange_FailsWithCk103AndCleanReport()
    {
        WriteSource("1.0.0");
        await PublishBaselineAsync();
        WriteSource("1.1.0", dependency: "Missing-[1.0,2.0)");

        var exception = await Assert.ThrowsAsync<ModelValidationException>(
            () => RunAsync("-p", _sourceDir, "-o", _reportPath));

        // The FR-9 check must fire before the compile stage aborts on the unresolvable
        // dependency — as a clean OCTO-CK103 finding, not a raw resolver exception.
        Assert.Contains("OCTO-CK103", exception.Message);
        Assert.Contains("Missing-[1.0,2.0)", exception.Message);

        var report = await File.ReadAllTextAsync(_reportPath, TestContext.Current.CancellationToken);
        Assert.Contains("OCTO-CK103", report);
        Assert.Contains("ERROR", report);
    }

    [Fact]
    public async Task UnknownCatalogName_WithRefresh_FailsWithAvailableCatalogList()
    {
        WriteSource("1.0.0");

        var exception = await Assert.ThrowsAsync<ModelValidationException>(
            () => RunAsync("-p", _sourceDir, "-cn", "Foo", "-rf"));

        Assert.Contains("Available catalogs", exception.Message);
        Assert.Contains("LocalFileSystemCatalog", exception.Message);
    }

    [Fact]
    public async Task VersionTooLow_ReportsCk100_AndSkipsMigrationReconciliation()
    {
        WriteSource("1.0.0");
        await PublishBaselineAsync();
        // Breaking change (enum value removed), version left untouched
        WriteSource("1.0.0", removeEnumValue: true);

        var exception = await Assert.ThrowsAsync<ModelValidationException>(
            () => RunAsync("-p", _sourceDir, "-o", _reportPath));

        Assert.Contains("OCTO-CK100", exception.Message);

        // While the version itself is invalid, the migration reconciliation must not run —
        // it would name the (wrong) declared version as the missing migration's toVersion.
        var report = await File.ReadAllTextAsync(_reportPath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("migration", report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MajorBumpWithoutMigration_WarnsWithDeclaredToVersion()
    {
        WriteSource("1.0.0");
        await PublishBaselineAsync();
        WriteSource("2.0.0", removeEnumValue: true);

        await RunAsync("-p", _sourceDir, "-o", _reportPath);

        var report = await File.ReadAllTextAsync(_reportPath, TestContext.Current.CancellationToken);
        Assert.Contains("VALID", report);
        Assert.Contains("toVersion 2.0.0", report);
    }

    [Fact]
    public async Task MajorBumpWithoutMigration_WithRequireFlag_FailsWithCk104()
    {
        WriteSource("1.0.0");
        await PublishBaselineAsync();
        WriteSource("2.0.0", removeEnumValue: true);

        var exception = await Assert.ThrowsAsync<ModelValidationException>(
            () => RunAsync("-p", _sourceDir, "-rmm"));

        Assert.Contains("OCTO-CK104", exception.Message);
    }

    [Fact]
    public async Task FailedValidation_DoesNotWriteChangelog_DespiteFlag()
    {
        WriteSource("1.0.0");
        await PublishBaselineAsync();
        WriteSource("1.0.0", removeEnumValue: true);

        await Assert.ThrowsAsync<ModelValidationException>(
            () => RunAsync("-p", _sourceDir, "-cl"));

        Assert.False(File.Exists(Path.Combine(_sourceDir, "CHANGELOG.md")),
            "CHANGELOG.md must not be written when validation fails");
    }

    [Fact]
    public async Task SuccessfulValidation_WithoutFlag_DoesNotWriteChangelog()
    {
        WriteSource("1.0.0");
        await PublishBaselineAsync();
        WriteSource("2.0.0", removeEnumValue: true);

        await RunAsync("-p", _sourceDir);

        Assert.False(File.Exists(Path.Combine(_sourceDir, "CHANGELOG.md")),
            "CHANGELOG.md must not be written without the --changelog flag");
    }

    [Fact]
    public async Task SuccessfulValidation_WithFlag_WritesChangelogSection()
    {
        WriteSource("1.0.0");
        await PublishBaselineAsync();
        WriteSource("2.0.0", removeEnumValue: true);

        await RunAsync("-p", _sourceDir, "-cl");

        var changelogPath = Path.Combine(_sourceDir, "CHANGELOG.md");
        Assert.True(File.Exists(changelogPath));
        var changelog = await File.ReadAllTextAsync(changelogPath, TestContext.Current.CancellationToken);
        Assert.Contains("## 2.0.0", changelog);
        Assert.Contains("### Breaking", changelog);
    }
}
