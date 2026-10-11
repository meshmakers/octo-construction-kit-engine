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
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     A catalog held in memory, standing in for a GitHub catalog under a configurable name. Matches model names
///     case-sensitively like the real cache-backed catalogs.
/// </summary>
internal sealed class NamedStubCatalog(string name, int order) : ICatalog
{
    private readonly Dictionary<CkModelId, CkCompiledModelRoot> _models = new();

    public int Order => order;
    public string CatalogName => name;
    public string Description => "In-memory catalog (tests)";
    public bool CanWrite => true;
    public bool CanRead => true;

    public void Seed(CkCompiledModelRoot model) => _models[model.ModelId] = model;

    public Task RefreshCatalogAsync(object? sourceIdentifier = null, bool forceRefresh = false) => Task.CompletedTask;
    public bool IsSupportingSourceIdentifier(object? sourceIdentifier = null) => sourceIdentifier == null;

    public Task<ModelExistingResult> IsExistingAsync(CkModelIdVersionRange range, object? sourceIdentifier = null)
    {
        var highest = _models.Keys.Where(id => id.Name == range.Name && range.IsSatisfiedBy(id))
            .OrderByDescending(id => id.Version).FirstOrDefault();
        return Task.FromResult(new ModelExistingResult
        {
            Exists = highest != null, ModelId = highest, CatalogName = name
        });
    }

    public Task<bool> IsExistingAsync(CkModelId modelId, object? sourceIdentifier = null) =>
        Task.FromResult(_models.ContainsKey(modelId));

    public Task<CkCompiledModelRoot> GetAsync(CkModelId modelId, OperationResult operationResult,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null) =>
        _models.TryGetValue(modelId, out var model)
            ? Task.FromResult(model)
            : throw ModelCatalogException.ModelNotFound(modelId, name);

    public Task PublishAsync(CkCompiledModelRoot ckCompiledModel, bool force = false, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        Seed(ckCompiledModel);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<CatalogResultItem> ListAsync(object? sourceIdentifier)
    {
        await Task.CompletedTask;
        foreach (var model in _models.Values.ToList())
        {
            yield return new CatalogResultItem { CatalogName = name, ModelId = model.ModelId, Description = model.Description };
        }
    }

    public IAsyncEnumerable<CatalogResultItem> SearchAsync(string searchTerm, object? sourceIdentifier) =>
        ListAsync(sourceIdentifier);
}

/// <summary>
///     AB#4469: <c>octo-ckc -c ValidateCatalog</c> and the service behind it, against real compiled models: the
///     synthetic graph of the cascade dry run (AB#5437: base <c>System</c>, the range-retaining <c>PlantR</c>, the
///     exact-pinned <c>PlantE</c> and <c>LineR</c>, which depends on <c>PlantR</c> only) in a local catalog and in
///     in-memory stand-ins for the GitHub catalogs. No network, nothing is published to a shared catalog (O1).
/// </summary>
public sealed class ValidateCatalogTests : IDisposable
{
    private const string Private = "PrivateGitHubCatalog";
    private const string Public = "PublicGitHubCatalog";
    private const string Other = "OtherStubCatalog";

    private readonly IEnvironmentService _environment = A.Fake<IEnvironmentService>();
    private readonly NamedStubCatalog _private = new(Private, 15);
    private readonly NamedStubCatalog _public = new(Public, 20);
    private readonly NamedStubCatalog _other = new(Other, 30);
    private readonly CkCompileFixture _fixture;

    public ValidateCatalogTests()
    {
        _fixture = new CkCompileFixture(services =>
        {
            // The real GitHub catalogs are disabled in the fixture; replace them with named in-memory stand-ins.
            services.RemoveAll<ICatalog>();
            services.AddTransient<ICatalog, LocalFileSystemCatalog>();
            services.AddTransient<ICatalog, EmbeddedResourceCatalog>();
            services.AddSingleton<ICatalog>(_private);
            services.AddSingleton<ICatalog>(_public);
            services.AddSingleton<ICatalog>(_other);
            services.AddSingleton(_environment);
            services.AddSingleton(A.Fake<IConsoleService>());
            services.AddSingleton<IParserService, ParserService>();
            services.AddSingleton<ICommandParser, CommandParser>();
            services.AddTransient<ICommand, ValidateCatalogCommand>();
        });
    }

    public void Dispose() => _fixture.Dispose();

    // ---- fixture models (as in CkCascadeTests) ------------------------------------------------------------------

    private string WriteSystem(string version, string[] attributes, string[] entityAttributes, int? ckLanguage = null)
    {
        var files = new Dictionary<string, string>
        {
            ["attributes/attributes.yaml"] = "attributes:\n" +
                                             string.Concat(attributes.Select(a => $"  - id: {a}\n    valueType: String\n")),
            ["types/entity.yaml"] = "types:\n  - typeId: Entity\n    isAbstract: true\n" +
                                    (ckLanguage == 2 ? "    derivable: Any\n" : "") +
                                    "    attributes:\n" +
                                    string.Concat(entityAttributes.Select(a =>
                                        $"      - id: ${{this}}/{a}\n        name: {a}\n        isOptional: true\n"))
        };
        return _fixture.WriteSource($"system-{version}-{Guid.NewGuid():N}", $"System-{version}", null, files, ckLanguage);
    }

    private string WritePlant(string name, string systemRange) =>
        _fixture.WriteSource($"{name}-{Guid.NewGuid():N}", $"{name}-1.0.0", [systemRange],
            new Dictionary<string, string>
            {
                ["attributes/attributes.yaml"] = "attributes:\n  - id: Serial\n    valueType: String\n",
                ["types/machine.yaml"] = "types:\n  - typeId: Machine\n    derivedFromCkTypeId: ${System}/Entity\n    attributes:\n" +
                                         "      - id: ${System}/Description\n        name: Description\n        isOptional: true\n" +
                                         "      - id: ${this}/Serial\n        name: Serial\n        isOptional: true\n"
            });

    private string WriteLine() =>
        _fixture.WriteSource($"line-{Guid.NewGuid():N}", "LineR-1.0.0", ["PlantR-[1.0,2.0)"],
            new Dictionary<string, string> { ["types/line.yaml"] = "types:\n  - typeId: Line\n    derivedFromCkTypeId: ${PlantR}/Machine\n" });

    private async Task PublishLocalAsync(string dir, bool rangeRetention)
    {
        _fixture.Services.GetRequiredService<IOptions<CkCompilerOptions>>().Value.RangeRetention = rangeRetention;
        await _fixture.CompileAndPublishAsync(dir);
    }

    /// <summary>System-2.2.0 with PlantR (ranges), PlantE (exact pins) and LineR, in the local catalog.</summary>
    private async Task PublishWorldAsync()
    {
        await PublishLocalAsync(WriteSystem("2.2.0", ["Name", "Description"], ["Name"]), false);
        await PublishLocalAsync(WritePlant("PlantR", "System-[2.2,3.0)"), true);
        await PublishLocalAsync(WritePlant("PlantE", "System-[2.2,3.0)"), false);
        await PublishLocalAsync(WriteLine(), true);
    }

    private async Task<CkCompiledModelRoot> LocalAsync(string name, string version)
    {
        var id = new CkModelId(name, version);
        return await _fixture.Services.GetRequiredService<ICatalogService>()
            .GetAsync(LocalFileSystemCatalog.Name, id, new OperationResult()) ?? throw new InvalidOperationException(id.FullName);
    }

    private async Task SeedAsync(NamedStubCatalog catalog, params (string Name, string Version)[] models)
    {
        foreach (var (name, version) in models)
        {
            catalog.Seed(await LocalAsync(name, version));
        }
    }

    private Task<CkCatalogValidationResult> ValidateAsync(string catalog, params string[] models) =>
        _fixture.Services.GetRequiredService<ICkCatalogValidationService>().ValidateAsync(catalog, models);

    private Task RunAsync(params string[] arguments)
    {
        A.CallTo(() => _environment.GetCommandLineArgs())
            .Returns(new[] { "octo-ckc", "-c", "ValidateCatalog" }.Concat(arguments).ToArray());
        return _fixture.Services.GetRequiredService<ICommandParser>().ParseAndValidateAsync();
    }

    private static CkCatalogModelCheck Model(CkCatalogValidationResult result, string name) =>
        Assert.Single(result.Models, m => m.Model.Name == name);

    private static string Snapshot(string directory) =>
        string.Join("|", Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).OrderBy(f => f)
            .Select(f => $"{Path.GetRelativePath(directory, f)}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))}"));

    // ---- acceptance criteria ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_consistent_catalog_resolves_everywhere_and_the_command_exits_cleanly()
    {
        await PublishWorldAsync();
        await SeedAsync(_private, ("System", "2.2.0"), ("PlantR", "1.0.0"), ("PlantE", "1.0.0"), ("LineR", "1.0.0"));
        var report = Path.Combine(_fixture.Root, "catalog.md");

        var result = await ValidateAsync(Private);
        await RunAsync("-cn", Private, "-o", report);

        Assert.False(result.HasDefects);
        Assert.Equal(["LineR", "PlantE", "PlantR", "System"], result.Models.Select(m => m.Model.Name).Order().ToArray());
        Assert.All(result.Models, m => Assert.True(m.IsResolved));
        var markdown = await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken);
        Assert.All(new[] { "System-2.2.0", "PlantR-1.0.0", "PlantE-1.0.0", "LineR-1.0.0" },
            id => Assert.Contains($"`{id}`", markdown));
        Assert.Contains("**Resolved**", markdown);
    }

    [Fact]
    public async Task A_pinned_dependency_that_is_missing_is_reported_and_fails_the_Meshmakers_catalog()
    {
        await PublishWorldAsync();
        await SeedAsync(_private, ("PlantE", "1.0.0")); // pins System-2.2.0, which the catalog does not have

        var result = await ValidateAsync(Private);

        var plant = Model(result, "PlantE");
        Assert.False(plant.IsResolved);
        Assert.Contains(plant.Findings, f => f.Contains("PlantE-1.0.0") && f.Contains("System-2.2.0") && f.Contains("no version of System"));
        Assert.True(result.HasErrors);
        var exception = await Assert.ThrowsAsync<ModelValidationException>(() => RunAsync("-cn", Private));
        Assert.Contains("1 model(s) do not resolve", exception.Message);
    }

    [Fact]
    public async Task A_pin_to_a_version_the_catalog_lacks_names_the_available_versions()
    {
        await PublishWorldAsync();
        await PublishLocalAsync(WriteSystem("2.3.0", ["Name", "Description"], ["Name"]), false);
        await SeedAsync(_private, ("System", "2.3.0"), ("PlantE", "1.0.0")); // pin System-2.2.0 missing, 2.3.0 present

        var result = await ValidateAsync(Private);

        Assert.Contains(Model(result, "PlantE").Findings, f => f.Contains("System-2.2.0") && f.Contains("2.3.0"));
    }

    [Fact]
    public async Task A_removed_member_the_range_retaining_model_uses_is_reported_naming_the_member()
    {
        await PublishWorldAsync();
        // System-2.3.0 drops the attribute definition Description that PlantR assigns to its Machine.
        await PublishLocalAsync(WriteSystem("2.3.0", ["Name"], ["Name"]), false);
        await SeedAsync(_private, ("System", "2.2.0"), ("System", "2.3.0"), ("PlantR", "1.0.0"));

        var result = await ValidateAsync(Private);

        var plant = Model(result, "PlantR");
        Assert.False(plant.IsResolved);
        Assert.Contains(plant.Findings, f => f.Contains("System@2/Description-1") && f.Contains("does not define"));
        Assert.True(result.HasErrors);
    }

    [Fact]
    public async Task A_used_member_that_the_newest_dependency_no_longer_declares_is_named()
    {
        await PublishWorldAsync();
        await PublishLocalAsync(WriteSystem("2.3.0", ["Name", "Description"], ["Description"]), false); // Entity loses the Name assignment
        var plantR = await LocalAsync("PlantR", "1.0.0");
        plantR.DependencyRanges![0].UsedSurface = ["System@2/Entity-1.Name"];
        await SeedAsync(_private, ("System", "2.2.0"), ("System", "2.3.0"));
        _private.Seed(plantR);

        var result = await ValidateAsync(Private);

        Assert.Contains(Model(result, "PlantR").Findings, f => f.Contains("'Name'") && f.Contains("System-2.3.0"));
    }

    [Fact]
    public async Task A_newer_major_outside_the_range_is_not_in_range_and_does_not_fail()
    {
        await PublishWorldAsync();
        await PublishLocalAsync(WriteSystem("3.0.0", ["Name"], ["Name"]), false);
        await SeedAsync(_private, ("System", "2.2.0"), ("System", "3.0.0"), ("PlantR", "1.0.0"));

        var result = await ValidateAsync(Private);
        await RunAsync("-cn", Private);

        var plant = Model(result, "PlantR");
        Assert.True(plant.IsResolved);
        Assert.Contains(plant.Notes, n => n.StartsWith("not in range") && n.Contains("System-3.0.0"));
        Assert.Contains("not in range", CkCatalogValidationReport.RenderConsole(result));
        Assert.False(result.HasErrors);
    }

    [Fact]
    public async Task No_installed_version_inside_the_range_is_a_defect()
    {
        await PublishWorldAsync();
        await PublishLocalAsync(WriteSystem("3.0.0", ["Name"], ["Name"]), false);
        await SeedAsync(_private, ("System", "3.0.0"), ("PlantR", "1.0.0")); // only a major outside the range

        var result = await ValidateAsync(Private);

        Assert.Contains(Model(result, "PlantR").Findings, f => f.Contains("no version in the catalog") || f.Contains("which no version"));
    }

    [Fact]
    public async Task Severity_follows_the_catalog_the_same_defect_is_an_error_in_Meshmakers_catalogs_and_a_warning_elsewhere()
    {
        await PublishWorldAsync();
        foreach (var catalog in new[] { _private, _public, _other })
        {
            await SeedAsync(catalog, ("PlantE", "1.0.0")); // missing System-2.2.0
        }

        var privateResult = await ValidateAsync(Private);
        var publicResult = await ValidateAsync(Public);
        var otherResult = await ValidateAsync(Other);

        Assert.True(privateResult.HasErrors);
        Assert.True(publicResult.HasErrors);
        Assert.True(otherResult.HasDefects);
        Assert.False(otherResult.HasErrors);
        await Assert.ThrowsAsync<ModelValidationException>(() => RunAsync("-cn", Public));
        await RunAsync("-cn", Other); // warnings only: exit code 0
        Assert.Contains("WARNING", CkCatalogValidationReport.RenderConsole(otherResult));
        Assert.Contains("ERROR", CkCatalogValidationReport.RenderConsole(privateResult));
    }

    [Fact]
    public async Task The_scope_is_the_named_model_and_its_dependents()
    {
        await PublishWorldAsync();
        await PublishLocalAsync(_fixture.WriteSource("unrelated", "Unrelated-1.0.0", null,
            new Dictionary<string, string> { ["attributes/a.yaml"] = "attributes:\n  - id: Free\n    valueType: String\n" }), false);
        await SeedAsync(_private, ("System", "2.2.0"), ("PlantR", "1.0.0"), ("PlantE", "1.0.0"), ("LineR", "1.0.0"), ("Unrelated", "1.0.0"));

        var scoped = await ValidateAsync(Private, "System");
        var line = await ValidateAsync(Private, "plantr"); // names match case-insensitively

        Assert.Equal(["LineR", "PlantE", "PlantR", "System"], scoped.Models.Select(m => m.Model.Name).Order().ToArray());
        Assert.Equal(["LineR", "PlantR"], line.Models.Select(m => m.Model.Name).Order().ToArray());
    }

    [Fact]
    public async Task An_unknown_model_name_fails_with_a_clear_message()
    {
        await PublishWorldAsync();
        await SeedAsync(_private, ("System", "2.2.0"));

        var exception = await Assert.ThrowsAsync<ModelValidationException>(() => RunAsync("-cn", Private, "-m", "NoSuchModel"));

        Assert.Contains("'NoSuchModel' is not in catalog 'PrivateGitHubCatalog'", exception.Message);
        Assert.Contains("System", exception.Message);
    }

    [Fact]
    public async Task Both_catalog_roots_are_read_and_a_v3_model_depending_on_a_v2_model_resolves()
    {
        await PublishWorldAsync(); // System-2.2.0 under ck-models/v2, the range-retaining PlantR under ck-models/v3
        var catalogDir = _fixture.CatalogDir;

        Assert.True(Directory.Exists(Path.Combine(catalogDir, "ck-models", "v2")));
        Assert.True(Directory.Exists(Path.Combine(catalogDir, "ck-models", "v3")));
        var result = await ValidateAsync(LocalFileSystemCatalog.Name);

        Assert.Contains(result.Models, m => m.Model.Name == "PlantR" && m.IsRangeRetaining && m.IsResolved);
        Assert.Contains(result.Models, m => m.Model.Name == "System" && m.IsResolved);
        Assert.False(result.HasDefects);
    }

    // ---- case-only name variants --------------------------------------------------------------------------------

    private static CkCompiledModelRoot Flat(string name, string version, int? ckLanguage) => new()
    {
        ModelId = new CkModelId(name, version), CkLanguage = ckLanguage,
        Attributes = [new CkAttributeDto { AttributeId = "Serial", ValueType = AttributeValueTypesDto.String }]
    };

    [Fact]
    public async Task Case_only_name_variants_in_the_same_root_are_reported_with_both_spellings_and_paths()
    {
        _private.Seed(Flat("System.Bot", "3.1.0", null));
        _private.Seed(Flat("system.bot", "3.2.0", null));
        _other.Seed(Flat("System.Bot", "3.1.0", null));
        _other.Seed(Flat("system.bot", "3.2.0", null));

        var result = await ValidateAsync(Private);

        var variants = Assert.Single(result.CaseVariants).Variants;
        Assert.Equal(["System.Bot", "system.bot"], variants.Select(v => v.Spelling).ToArray());
        Assert.Equal("ck-models/v2/s/System.Bot/", variants[0].CatalogPath);
        Assert.Equal("ck-models/v2/s/system.bot/", variants[1].CatalogPath);
        Assert.True(result.HasErrors);
        var console = CkCatalogValidationReport.RenderConsole(result);
        Assert.Contains("System.Bot (newest 3.1.0) at ck-models/v2/s/System.Bot/", console);
        Assert.Contains("system.bot (newest 3.2.0) at ck-models/v2/s/system.bot/", console);

        var warning = await ValidateAsync(Other);
        Assert.Single(warning.CaseVariants);
        Assert.False(warning.HasErrors);
        await RunAsync("-cn", Other);
        await Assert.ThrowsAsync<ModelValidationException>(() => RunAsync("-cn", Private));
    }

    [Fact]
    public async Task Case_only_name_variants_across_the_roots_are_reported_with_the_path_of_each()
    {
        _private.Seed(Flat("System.Bot", "3.1.0", null));  // ckLanguage 1: ck-models/v2
        _private.Seed(Flat("system.bot", "3.2.0", 2));      // ckLanguage 2: ck-models/v3

        var result = await ValidateAsync(Private);

        var variants = Assert.Single(result.CaseVariants).Variants;
        Assert.Equal("ck-models/v2/s/System.Bot/", variants.Single(v => v.Spelling == "System.Bot").CatalogPath);
        Assert.Equal("ck-models/v3/s/system.bot/", variants.Single(v => v.Spelling == "system.bot").CatalogPath);
        Assert.True(result.HasErrors);
    }

    [Fact]
    public async Task A_catalog_without_case_only_pairs_reports_nothing_new()
    {
        _private.Seed(Flat("System.Bot", "3.1.0", null));
        _private.Seed(Flat("System.Bots", "1.0.0", null));

        var result = await ValidateAsync(Private);

        Assert.Empty(result.CaseVariants);
        Assert.False(result.HasDefects);
        Assert.DoesNotContain("case-only", CkCatalogValidationReport.RenderConsole(result));
        Assert.DoesNotContain("case-only", CkCatalogValidationReport.RenderMarkdown(result));
    }

    // ---- read-only, offline, parity with the dry run --------------------------------------------------------------

    [Fact]
    public async Task The_check_is_read_only_and_works_offline_against_the_local_catalog()
    {
        await PublishWorldAsync();
        var before = Snapshot(_fixture.CatalogDir);

        await RunAsync("-cn", LocalFileSystemCatalog.Name);
        await RunAsync("-cn", LocalFileSystemCatalog.Name, "-m", "System", "-o", Path.Combine(_fixture.Root, "r.md"));

        Assert.Equal(before, Snapshot(_fixture.CatalogDir));
    }

    [Fact]
    public async Task For_the_cascade_graph_the_verdicts_agree_with_the_dry_run()
    {
        await PublishWorldAsync();
        var candidateDir = WriteSystem("2.3.0", ["Name"], ["Name"]); // removes Description
        _fixture.Services.GetRequiredService<IOptions<CkCompilerOptions>>().Value.RangeRetention = false;
        var dryRun = await _fixture.Services.GetRequiredService<ICkCascadeService>()
            .AnalyzeAsync(await _fixture.CompileAsync(candidateDir));
        await PublishLocalAsync(candidateDir, false);

        var catalog = await ValidateAsync(LocalFileSystemCatalog.Name);

        // Range-retaining dependents: Breaks <-> does not resolve, Compatible <-> resolves.
        foreach (var name in new[] { "PlantR", "LineR" })
        {
            var verdict = dryRun.Dependents.Single(d => d.Dependent.Name == name).Verdict;
            Assert.Equal(verdict == CkDependentVerdict.Breaks, !Model(catalog, name).IsResolved);
        }

        Assert.Equal(CkDependentVerdict.Breaks, dryRun.Dependents.Single(d => d.Dependent.Name == "PlantR").Verdict);
        Assert.Equal(dryRun.Dependents.Single(d => d.Dependent.Name == "PlantR").Reasons
                .Where(r => r.Contains("does not define")).ToArray(),
            Model(catalog, "PlantR").Findings.Where(f => f.Contains("does not define")).ToArray());
        // An exact pin to a published version still resolves; the dry run's Breaks/NeedsRepin is about re-pinning.
        Assert.True(Model(catalog, "PlantE").IsResolved);
    }
}
