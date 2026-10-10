using FakeItEasy;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     Command-level tests for <see cref="ValidateVersionCommand" />: the command is executed
///     in-process through the real command parser (arguments injected via a faked
///     <see cref="IEnvironmentService" />) against real engine services and a
///     <c>LocalFileSystemCatalog</c> in a temp directory plus an in-memory published catalog
///     (<see cref="InMemoryPublishedCatalog" />, standing in for the GitHub catalogs) — no network, no fakes
///     below the command boundary. Covers the error paths that have no engine-level test surface:
///     OCTO-CK103, the unknown-catalog refresh failure, migration reconciliation
///     skip/escalation, the changelog write gating and (AB#5450) the per-major baseline.
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
        services.AddSingleton<ICatalog, InMemoryPublishedCatalog>();

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
    private void WriteSource(string version, bool removeEnumValue = false, string? dependency = null,
        int? ckLanguage = null, string? description = null)
    {
        Directory.CreateDirectory(Path.Combine(_sourceDir, "attributes"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "enums"));

        var dependencies = dependency == null ? "" : $"dependencies:\n  - {dependency}\n";
        File.WriteAllText(Path.Combine(_sourceDir, "ckModel.yaml"),
            "\"$schema\": \"https://schemas.meshmakers.cloud/construction-kit-meta.schema.json\"\n" +
            dependencies +
            (ckLanguage == null ? "" : $"ckLanguage: {ckLanguage}\n") +
            (description == null ? "" : $"description: {description}\n") +
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
    ///     Publishes the current source as the baseline into the in-memory published catalog (default) or another
    ///     catalog, e.g. the temp local catalog to simulate an earlier local build.
    /// </summary>
    private async Task PublishBaselineAsync(string catalogName = InMemoryPublishedCatalog.Name)
    {
        var operationResult = new OperationResult();
        var compiled = await _serviceProvider.GetRequiredService<ICompilerService>()
            .CompileInMemoryAsync(_sourceDir, operationResult);
        Assert.False(operationResult.HasErrors);

        await _serviceProvider.GetRequiredService<ICatalogService>().PublishAsync(
            catalogName, compiled, new OriginFileResolver(_sourceDir), isForced: true);
    }

    private Task RunAsync(params string[] arguments)
    {
        A.CallTo(() => _environment.GetCommandLineArgs())
            .Returns(new[] { "octo-ckc", "-c", "ValidateVersion" }.Concat(arguments).ToArray());
        return _serviceProvider.GetRequiredService<ICommandParser>().ParseAndValidateAsync();
    }

    /// <summary>
    ///     Writes a dependent CK source into its own directory: it declares a dependency range on
    ///     <c>CmdFixture</c> and one type deriving from a <c>CmdFixture</c> type is not needed —
    ///     the dependency declaration alone exercises resolution.
    /// </summary>
    private string WriteDependentSource(string version, string dependencyRange)
    {
        var dependentDir = Path.Combine(_root, "src-dependent");
        Directory.CreateDirectory(dependentDir);
        Directory.CreateDirectory(Path.Combine(dependentDir, "attributes"));

        File.WriteAllText(Path.Combine(dependentDir, "ckModel.yaml"),
            "\"$schema\": \"https://schemas.meshmakers.cloud/construction-kit-meta.schema.json\"\n" +
            $"dependencies:\n  - {dependencyRange}\n" +
            $"modelId: CmdFixtureDependent-{version}\n");

        File.WriteAllText(Path.Combine(dependentDir, "attributes", "label.yaml"),
            "\"$schema\": \"https://schemas.meshmakers.cloud/construction-kit-elements.schema.json\"\n" +
            "attributes:\n  - id: Label\n    valueType: String\n");

        return dependentDir;
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

    [Fact]
    public async Task DependencyOnSiblingVersionBumpedInSameRun_IsSatisfiedWithoutCk103()
    {
        // Baselines: CmdFixture-1.0.0 and CmdFixtureDependent-1.0.0 (dep range open at 1.0)
        WriteSource("1.0.0");
        await PublishBaselineAsync();
        var dependentDir = WriteDependentSource("1.0.0", "CmdFixture-[1.0,2.0)");
        await PublishDependentBaselineAsync(dependentDir);

        // Same-commit bump: CmdFixture 1.0.0 -> 1.1.0 (additive), dependent narrows to [1.1,2.0)
        // — the range is satisfied by NO published version, only by the sibling working copy.
        WriteSource("1.1.0");
        WriteDependentSource("1.1.0", "CmdFixture-[1.1,2.0)");

        await RunAsync("-p", _sourceDir, "-p", dependentDir, "-o", _reportPath);

        // The sibling is registered in the local catalog after its own validation, so both the
        // dependency-existence check and the dependent's compile resolve CmdFixture-1.1.0 even
        // though it was never published before this run.
        var report = await File.ReadAllTextAsync(_reportPath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("OCTO-CK103", report);
        Assert.DoesNotContain("ERROR", report);
    }

    [Fact]
    public async Task DependencyRangeNotSatisfiedBySibling_StillFailsWithCk103()
    {
        WriteSource("1.0.0");
        await PublishBaselineAsync();
        var dependentDir = WriteDependentSource("1.0.0", "CmdFixture-[1.0,2.0)");
        await PublishDependentBaselineAsync(dependentDir);

        // Sibling bumps to 1.1.0 but the dependent demands [2.0,3.0) — the sibling must NOT
        // satisfy the range, the honest OCTO-CK103 stays.
        WriteSource("1.1.0");
        WriteDependentSource("1.1.0", "CmdFixture-[2.0,3.0)");

        var exception = await Assert.ThrowsAsync<ModelValidationException>(
            () => RunAsync("-p", _sourceDir, "-p", dependentDir, "-o", _reportPath));

        Assert.Contains("OCTO-CK103", exception.Message);
        Assert.Contains("CmdFixture-[2.0,3.0)", exception.Message);
    }

    [Fact]
    public async Task DependencyOnFirstPublicationSibling_IsSatisfiedWithoutCk103()
    {
        // CmdFixture has never been published (first publication) and the dependent — also new —
        // depends on it in the very same run.
        WriteSource("1.0.0");
        var dependentDir = WriteDependentSource("1.0.0", "CmdFixture-[1.0,2.0)");

        await RunAsync("-p", _sourceDir, "-p", dependentDir, "-o", _reportPath);

        var report = await File.ReadAllTextAsync(_reportPath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("OCTO-CK103", report);
        Assert.Contains("First publication", report);
        // The dependent's sibling registration must have compiled against the brand-new
        // CmdFixture-1.0.0 the first package registered a moment earlier.
        Assert.DoesNotContain("could not be compiled for sibling dependency resolution", report);
        Assert.DoesNotContain("could not be registered for sibling dependency resolution", report);
    }

    // ── AB#5450: the baseline is the newest published version of the same major ─────────────────────────

    [Fact]
    public async Task OlderMajorLine_ValidatesAgainstTheNewestVersionOfItsMajor()
    {
        WriteSource("3.3.0", removeEnumValue: true);
        await PublishBaselineAsync();
        WriteSource("4.5.0", removeEnumValue: true);
        await PublishBaselineAsync();
        // Additive change (enum value added) on the 3.x line
        WriteSource("3.4.0");

        await RunAsync("-p", _sourceDir, "-o", _reportPath);

        var report = await File.ReadAllTextAsync(_reportPath, TestContext.Current.CancellationToken);
        Assert.Contains("VALID", report);
        Assert.Contains("- Published: `3.3.0` (TestPublishedCatalog)", report);
        Assert.Contains("**MINOR** bump → minimum version `3.4.0`", report);
        Assert.DoesNotContain("OCTO-CK101", report);
    }

    [Fact]
    public async Task OlderMajorLine_DowngradeWithinTheMajor_FailsWithCk101AgainstThatMajor()
    {
        WriteSource("3.3.0");
        await PublishBaselineAsync();
        WriteSource("4.5.0");
        await PublishBaselineAsync();
        WriteSource("3.2.0");

        var exception = await Assert.ThrowsAsync<ModelValidationException>(
            () => RunAsync("-p", _sourceDir, "-o", _reportPath));

        Assert.Contains("OCTO-CK101: Declared version 3.2.0 of model 'CmdFixture' is lower than the published version 3.3.0",
            exception.Message);
    }

    [Fact]
    public async Task NewMajor_IsDiffedAgainstThePreviousMajorLine_MinorChangeIsAValidMajorWithoutStructuralNeed()
    {
        WriteSource("3.3.0", removeEnumValue: true);
        await PublishBaselineAsync();
        WriteSource("4.0.0");

        await RunAsync("-p", _sourceDir, "-o", _reportPath);

        var report = await File.ReadAllTextAsync(_reportPath, TestContext.Current.CancellationToken);
        Assert.Contains("VALID", report);
        Assert.Contains("- Published: `3.3.0`", report);
        Assert.Contains("New major line 4: the baseline is CmdFixture-3.3.0", report);
        Assert.Contains("Valid major bump without structural need", report);
        Assert.Contains("Enum value", report);
    }

    [Fact]
    public async Task NewMajor_BreakingChangeWithoutMigration_RunsTheMigrationCheckAgainstThePreviousLine()
    {
        WriteSource("3.3.0");
        await PublishBaselineAsync();
        WriteSource("4.0.0", removeEnumValue: true);

        var exception = await Assert.ThrowsAsync<ModelValidationException>(
            () => RunAsync("-p", _sourceDir, "-rmm"));

        Assert.Contains("OCTO-CK104", exception.Message);
    }

    [Fact]
    public async Task PublishedEqualVersion_WithStructuralChange_StillRequiresABump_AlsoWithALocalSelfCopy()
    {
        WriteSource("1.0.0");
        await PublishBaselineAsync();
        // The model under test, already in the local catalog from an earlier local build (AB#5434 symptom 2)
        WriteSource("1.0.0", removeEnumValue: true);
        await PublishBaselineAsync(LocalFileSystemCatalog.Name);

        var exception = await Assert.ThrowsAsync<ModelValidationException>(
            () => RunAsync("-p", _sourceDir, "-o", _reportPath));

        Assert.Contains("OCTO-CK100", exception.Message);
        var report = await File.ReadAllTextAsync(_reportPath, TestContext.Current.CancellationToken);
        Assert.Contains("- Published: `1.0.0` (TestPublishedCatalog)", report);
        Assert.Contains("ignored as baseline", report);
        Assert.Contains("CmdFixture-1.0.0", report);
    }

    [Fact]
    public async Task RepeatedRuns_WithRefresh_GiveTheSameBaselineAndVerdict()
    {
        WriteSource("1.0.0", removeEnumValue: true);
        await PublishBaselineAsync();
        WriteSource("1.1.0");

        // The first run registers CmdFixture-1.1.0 in the local catalog (sibling resolution); the second run must
        // not compare the model against that copy of itself.
        await RunAsync("-p", _sourceDir, "-rf", "-o", _reportPath);
        var first = await File.ReadAllTextAsync(_reportPath, TestContext.Current.CancellationToken);
        await RunAsync("-p", _sourceDir, "-rf", "-o", _reportPath);
        var second = await File.ReadAllTextAsync(_reportPath, TestContext.Current.CancellationToken);

        Assert.Contains("- Published: `1.0.0` (TestPublishedCatalog)", first);
        Assert.Contains("- Published: `1.0.0` (TestPublishedCatalog)", second);
        Assert.Contains("**MINOR** bump → minimum version `1.1.0`", first);
        Assert.Contains("**MINOR** bump → minimum version `1.1.0`", second);
        Assert.Contains("Local catalog entries at or above the declared version were ignored as baseline", second);
        Assert.Contains("CmdFixture-1.1.0", second);
    }

    [Fact]
    public async Task LocalEntryAtDeclaredVersionOnly_IsNotABaseline_FirstPublication()
    {
        WriteSource("1.0.0");
        await PublishBaselineAsync(LocalFileSystemCatalog.Name);
        WriteSource("1.0.0", removeEnumValue: true);

        await RunAsync("-p", _sourceDir, "-o", _reportPath);

        var report = await File.ReadAllTextAsync(_reportPath, TestContext.Current.CancellationToken);
        Assert.Contains("First publication", report);
        Assert.Contains("ignored as baseline", report);
    }

    [Fact]
    public async Task LocalBaselineBelowDeclared_IsMarkedLocalInTheReport()
    {
        WriteSource("1.0.0", removeEnumValue: true);
        await PublishBaselineAsync(LocalFileSystemCatalog.Name);
        WriteSource("1.1.0");

        await RunAsync("-p", _sourceDir, "-o", _reportPath);

        var report = await File.ReadAllTextAsync(_reportPath, TestContext.Current.CancellationToken);
        Assert.Contains("- Published: `1.0.0` (LocalFileSystemCatalog, local, not published)", report);
    }

    private async Task PublishDependentBaselineAsync(string dependentDir)
    {
        var operationResult = new OperationResult();
        var compiled = await _serviceProvider.GetRequiredService<ICompilerService>()
            .CompileInMemoryAsync(dependentDir, operationResult);
        Assert.False(operationResult.HasErrors);

        await _serviceProvider.GetRequiredService<ICatalogService>().PublishAsync(
            LocalFileSystemCatalog.Name, compiled, new OriginFileResolver(dependentDir), isForced: true);
    }

    /// <summary>
    ///     AB#6294 "same verdict everywhere": ValidateVersion and the compile gate share one verdict service, so for
    ///     the same model and baseline they agree on pass/fail, the required level and the minimum version.
    /// </summary>
    [Theory]
    [InlineData("2.4.0", "2.5.0", false)] // additive-free: nothing changed, bump without structural change
    [InlineData("2.4.0", "2.5.0", true)] // breaking, minor bump: too low
    [InlineData("2.4.0", "3.0.0", true)] // breaking, major bump: valid
    [InlineData("2.4.0", "2.4.0", true)] // breaking, version untouched
    [InlineData("2.4.0", "2.3.0", false)] // downgrade
    public async Task ValidateVersion_AndCompileGate_AgreeOnTheVerdict(string baselineVersion, string declaredVersion,
        bool removeEnumValue)
    {
        WriteSource(baselineVersion, ckLanguage: 2);
        await PublishBaselineAsync();
        WriteSource(declaredVersion, removeEnumValue, ckLanguage: 2);

        string? commandMinimum = null;
        var commandFailed = false;
        try
        {
            await RunAsync("-p", _sourceDir, "-o", _reportPath);
        }
        catch (ModelValidationException)
        {
            commandFailed = true;
        }

        var report = await File.ReadAllTextAsync(_reportPath, TestContext.Current.CancellationToken);
        var match = System.Text.RegularExpressions.Regex.Match(report, "minimum version `([0-9.]+)`");
        if (match.Success)
        {
            commandMinimum = match.Groups[1].Value;
        }

        var operationResult = new OperationResult();
        var current = await _serviceProvider.GetRequiredService<ICompilerService>()
            .CompileInMemoryAsync(_sourceDir, operationResult);
        var gate = await _serviceProvider.GetRequiredService<CkCompileGate>()
            .RunAsync(current, CkBaselineSource.Remote);

        Assert.Equal(commandFailed, gate.HasErrors);
        Assert.NotNull(gate.Verdict);
        if (commandMinimum != null)
        {
            Assert.Equal(commandMinimum, gate.Verdict.Validation.MinimumVersion.ToString());
        }
    }

    private const string AckKey = "RecordAttribute:Login-1/Secret#Modified:access";

    /// <summary>A ckLanguage 2 record with a security-sensitive attribute; the access and the acknowledgement vary.</summary>
    private void WriteAcknowledgeSource(string version, string access, string? reason = null, string key = AckKey)
    {
        Directory.CreateDirectory(Path.Combine(_sourceDir, "attributes"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "records"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "enums"));
        File.WriteAllText(Path.Combine(_sourceDir, "ckModel.yaml"),
            "\"$schema\": \"https://schemas.meshmakers.cloud/construction-kit-meta.schema.json\"\n" +
            $"ckLanguage: 2\nmodelId: CmdFixture-{version}\n" +
            (reason == null
                ? ""
                : $"compatibility:\n  acknowledge:\n    - change: \"{key}\"\n      reason: \"{reason}\"\n"));
        File.WriteAllText(Path.Combine(_sourceDir, "attributes", "secret.yaml"),
            "\"$schema\": \"https://schemas.meshmakers.cloud/construction-kit-elements.schema.json\"\n" +
            "attributes:\n  - id: Secret\n    valueType: String\n    securitySensitive: true\n");
        File.WriteAllText(Path.Combine(_sourceDir, "records", "login.yaml"),
            "\"$schema\": \"https://schemas.meshmakers.cloud/construction-kit-elements.schema.json\"\n" +
            "records:\n  - recordId: Login\n    attributes:\n      - id: ${this}/Secret\n        name: Secret\n" +
            $"        isOptional: true\n        access: {access}\n");
        File.WriteAllText(Path.Combine(_sourceDir, "enums", "state.yaml"),
            "\"$schema\": \"https://schemas.meshmakers.cloud/construction-kit-elements.schema.json\"\n" +
            "enums:\n  - enumId: State\n    values:\n      - key: 0\n        name: IdleState\n");
    }

    /// <summary>
    ///     AB#6295: ValidateVersion and the compile gate give the same acknowledge verdict (OCTO-CK203 / OCTO-CK204),
    ///     and ValidateVersion lists acknowledged changes in the report and the generated CHANGELOG.
    /// </summary>
    [Theory]
    [InlineData("Hidden", null, "OCTO-CK203")]
    [InlineData("Hidden", "Accepted risk R13", null)]
    [InlineData("ReadOnly", "Accepted risk R13", "OCTO-CK204")]
    public async Task ValidateVersion_AndCompileGate_AgreeOnTheAcknowledgeVerdict(string access, string? reason,
        string? expectedCode)
    {
        WriteAcknowledgeSource("2.4.0", "ReadOnly");
        await PublishBaselineAsync();
        WriteAcknowledgeSource("2.5.0", access, reason);

        string? commandMessage = null;
        try
        {
            await RunAsync("-p", _sourceDir, "-o", _reportPath, "-cl");
        }
        catch (ModelValidationException exception)
        {
            commandMessage = exception.Message;
        }

        var current = await _serviceProvider.GetRequiredService<ICompilerService>()
            .CompileInMemoryAsync(_sourceDir, new OperationResult());
        var gate = await _serviceProvider.GetRequiredService<CkCompileGate>()
            .RunAsync(current, CkBaselineSource.Remote);

        var gateCodes = gate.Messages.Where(m => m.Code != null && m.Severity == CkCompileGateSeverity.Error)
            .Select(m => m.Code!).ToArray();
        if (expectedCode == null)
        {
            Assert.Null(commandMessage);
            Assert.Empty(gateCodes);
            var report = await File.ReadAllTextAsync(_reportPath, TestContext.Current.CancellationToken);
            Assert.Contains("Acknowledged changes", report);
            Assert.Contains("Accepted risk R13", report);
            var changelog = await File.ReadAllTextAsync(Path.Combine(_sourceDir, "CHANGELOG.md"),
                TestContext.Current.CancellationToken);
            Assert.Contains("### Acknowledged changes", changelog);
            Assert.Contains("acknowledged: Accepted risk R13", changelog);
        }
        else
        {
            Assert.NotNull(commandMessage);
            Assert.Contains(expectedCode, commandMessage);
            Assert.Equal([expectedCode], gateCodes);
        }
    }

    // ---- AB#4467: --apply -------------------------------------------------------------------------------------

    private string MetadataPath => Path.Combine(_sourceDir, "ckModel.yaml");

    private string ReadMetadata() => File.ReadAllText(MetadataPath);

    private string ReadReport() => File.ReadAllText(_reportPath);

    [Fact]
    public async Task Apply_WritesTheMinimumVersion_ChangesOnlyTheVersionText_AndTheGatePassesAfterwards()
    {
        WriteSource("2.4.0", ckLanguage: 2);
        await PublishBaselineAsync();
        WriteSource("2.5.0", removeEnumValue: true, ckLanguage: 2);
        File.WriteAllText(MetadataPath, "# keep this comment\r\n" + ReadMetadata().Replace("\n", "\r\n") + "# trailing comment\r\n");
        var before = ReadMetadata();

        await RunAsync("-p", _sourceDir, "--apply", "-o", _reportPath);

        // File diff test: the whole file is byte-identical except for the version text.
        Assert.Equal(before.Replace("CmdFixture-2.5.0", "CmdFixture-3.0.0"), ReadMetadata());
        var report = ReadReport();
        Assert.Contains("2.5.0 → 3.0.0", report);
        Assert.Contains("VALID", report);

        // The following build (compile gate) passes.
        var current = await _serviceProvider.GetRequiredService<ICompilerService>()
            .CompileInMemoryAsync(_sourceDir, new OperationResult());
        var gate = await _serviceProvider.GetRequiredService<CkCompileGate>().RunAsync(current, CkBaselineSource.Remote);
        Assert.False(gate.HasErrors);
        Assert.Equal(new CkVersion(3, 0, 0), current.ModelId.Version);
    }

    [Theory]
    [InlineData("1.0.0", false, true, "1.1.0")] // v1, minor (enum value added)
    [InlineData("1.0.0", true, false, "1.0.1")] // v1, patch (model description changed)
    public async Task Apply_FixesCkLanguage1Models_Minor_And_Patch(string baselineVersion, bool withDescription,
        bool addsAttribute, string expected)
    {
        WriteSource(baselineVersion);
        await PublishBaselineAsync();
        // Minor: an extra attribute (additive); patch: the model description changed.
        WriteSource(baselineVersion, description: withDescription ? "changed text" : null);
        if (addsAttribute)
        {
            File.WriteAllText(Path.Combine(_sourceDir, "attributes", "extra.yaml"),
                "\"$schema\": \"https://schemas.meshmakers.cloud/construction-kit-elements.schema.json\"\n" +
                "attributes:\n  - id: Extra\n    valueType: String\n    isRuntimeState: false\n");
        }

        await RunAsync("-p", _sourceDir, "-ap", "-o", _reportPath);

        Assert.Contains($"modelId: CmdFixture-{expected}", ReadMetadata());
        Assert.Contains($"{baselineVersion} → {expected}", ReadReport());
    }

    [Fact]
    public async Task Apply_DoesNotWrite_WhenTheVerdictIsAlreadyValid()
    {
        WriteSource("2.4.0", ckLanguage: 2);
        await PublishBaselineAsync();
        WriteSource("2.5.0", ckLanguage: 2);
        var before = ReadMetadata();

        await RunAsync("-p", _sourceDir, "--apply", "-o", _reportPath);

        Assert.Equal(before, ReadMetadata());
        Assert.DoesNotContain("Applied", ReadReport());
    }

    [Fact]
    public async Task Apply_DoesNotWrite_OnADowngrade()
    {
        WriteSource("2.4.0", ckLanguage: 2);
        await PublishBaselineAsync();
        WriteSource("2.3.0", removeEnumValue: true, ckLanguage: 2);
        var before = ReadMetadata();

        var exception = await Assert.ThrowsAsync<ModelValidationException>(
            () => RunAsync("-p", _sourceDir, "--apply"));

        Assert.Contains("OCTO-CK101", exception.Message);
        Assert.Equal(before, ReadMetadata());
    }

    [Theory]
    [InlineData("Hidden", null, "OCTO-CK203")]
    [InlineData("ReadOnly", "Accepted risk R13", "OCTO-CK204")]
    public async Task Apply_DoesNotWrite_OnAMissingOrStaleAcknowledgement(string access, string? reason,
        string expectedCode)
    {
        WriteAcknowledgeSource("2.4.0", "ReadOnly");
        await PublishBaselineAsync();
        // Version too low as well (a patch bump over a Minor change), but the acknowledgement is the finding to fix.
        WriteAcknowledgeSource("2.4.1", access, reason);
        var before = ReadMetadata();

        var exception = await Assert.ThrowsAsync<ModelValidationException>(
            () => RunAsync("-p", _sourceDir, "--apply"));

        Assert.Contains(expectedCode, exception.Message);
        Assert.Equal(before, ReadMetadata());
    }

    [Fact]
    public async Task Apply_DoesNotWrite_OnACompileError()
    {
        WriteSource("2.4.0", ckLanguage: 2);
        await PublishBaselineAsync();
        WriteSource("2.5.0", removeEnumValue: true, ckLanguage: 2);
        File.WriteAllText(Path.Combine(_sourceDir, "enums", "broken.yaml"),
            "\"$schema\": \"https://schemas.meshmakers.cloud/construction-kit-elements.schema.json\"\n" +
            "enums:\n  - enumId: Broken\n    values: [ this is: not valid\n");
        var before = ReadMetadata();

        await Assert.ThrowsAnyAsync<Exception>(() => RunAsync("-p", _sourceDir, "--apply"));

        Assert.Equal(before, ReadMetadata());
    }

    [Fact]
    public async Task WithoutApply_TheFileIsNeverChanged()
    {
        WriteSource("2.4.0", ckLanguage: 2);
        await PublishBaselineAsync();
        WriteSource("2.5.0", removeEnumValue: true, ckLanguage: 2);
        var before = ReadMetadata();

        await Assert.ThrowsAsync<ModelValidationException>(() => RunAsync("-p", _sourceDir));

        Assert.Equal(before, ReadMetadata());
    }

    [Fact]
    public async Task Apply_TwoPackagesInDependencyOrder_TheDependentSeesTheSiblingsNewVersion()
    {
        WriteSource("1.0.0", ckLanguage: 2);
        await PublishBaselineAsync();
        var dependentDir = WriteDependentSource("1.0.0", "CmdFixture-[1.0,2.0)");
        await PublishDependentBaselineAsync(dependentDir);

        // CmdFixture breaks with a minor bump: --apply writes 2.0.0. The dependent already declares [2.0,3.0), which
        // only the applied sibling satisfies.
        WriteSource("1.1.0", removeEnumValue: true, ckLanguage: 2);
        WriteDependentSource("2.0.0", "CmdFixture-[2.0,3.0)");

        await RunAsync("-p", _sourceDir, "-p", dependentDir, "--apply", "-o", _reportPath);

        Assert.Contains("modelId: CmdFixture-2.0.0", ReadMetadata());
        var report = ReadReport();
        Assert.DoesNotContain("OCTO-CK103", report);
        Assert.DoesNotContain("ERROR", report);
    }

    [Fact]
    public async Task Apply_ReportsADependencyRangeThatExcludesANewerMajor_AndDoesNotChangeTheRange()
    {
        WriteSource("1.0.0");
        await PublishBaselineAsync();
        var dependentDir = WriteDependentSource("1.0.0", "CmdFixture-[1.0,2.0)");
        await PublishDependentBaselineAsync(dependentDir);
        WriteSource("3.0.0", removeEnumValue: true);
        await PublishBaselineAsync();
        WriteSource("1.0.0");
        var dependentBefore = File.ReadAllText(Path.Combine(dependentDir, "ckModel.yaml"));

        await RunAsync("-p", dependentDir, "--apply", "-o", _reportPath);

        Assert.True(ReadReport().Contains("not reconciled automatically"), ReadReport());
        Assert.Contains("CmdFixture-3.0.0", ReadReport());
        Assert.Contains("excludes", ReadReport());
        Assert.Equal(dependentBefore, File.ReadAllText(Path.Combine(dependentDir, "ckModel.yaml")));
    }

    [Fact]
    public async Task Apply_RefusesAFileThatIsNotUtf8_AndWritesNothing()
    {
        WriteSource("2.4.0", ckLanguage: 2);
        await PublishBaselineAsync();
        WriteSource("2.5.0", removeEnumValue: true, ckLanguage: 2);
        var latin1 = System.Text.Encoding.Latin1.GetBytes(ReadMetadata().Replace("ckLanguage: 2", "description: Prüfung für Maschinen\nckLanguage: 2"));
        File.WriteAllBytes(MetadataPath, latin1);

        await Assert.ThrowsAsync<ModelValidationException>(() => RunAsync("-p", _sourceDir, "--apply"));

        Assert.Equal(latin1, File.ReadAllBytes(MetadataPath));
        Assert.False(File.Exists(MetadataPath + ".apply.tmp"));
    }
}
