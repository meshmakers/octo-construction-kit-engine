using System.Collections;
using Meshmakers.Octo.ConstructionKit.MsBuildTasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     AB#6294 / AB#6295: the REAL <see cref="CkCompile" /> MSBuild task, executed with a recording build engine, a
///     local catalog in a temp directory and every remote catalog disabled. Proves that the compatibility gate fires
///     inside the task (not only in the gate class): it fails the task before anything is published, honours the
///     acknowledge path and rejects an invalid baseline source. Runs in normal CI.
/// </summary>
public sealed class CkCompileTaskTests : IDisposable
{
    private const string SecretKey = "RecordAttribute:Login-1/Secret#Modified:access";

    private readonly CkCompileFixture _fixture = new();
    private readonly string _catalogRoot;
    private int _run;

    public CkCompileTaskTests()
    {
        _catalogRoot = Path.Combine(_fixture.Root, "task-catalog");
        Directory.CreateDirectory(_catalogRoot);
    }

    public void Dispose() => _fixture.Dispose();

    private sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<string> Errors { get; } = [];
        public List<string> Messages { get; } = [];
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => "test.csproj";

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add($"{e.Code}: {e.Message}");
        public void LogWarningEvent(BuildWarningEventArgs e) => Messages.Add($"warning {e.Code}: {e.Message}");
        public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e.Message ?? "");
        public void LogCustomEvent(CustomBuildEventArgs e) { }

        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties,
            IDictionary targetOutputs) => true;
    }

    private string WriteModel(string version, string enumValues = "A,B", string access = "ReadOnly",
        string? acknowledge = null)
    {
        var values = string.Concat(enumValues.Split(',').Select((v, i) =>
            $"          - key: {i}\n            name: {v}\n"));
        return _fixture.WriteSource("task-model", $"TaskModel-{version}", null, new Dictionary<string, string>
        {
            ["attributes/attributes.yaml"] =
                "attributes:\n  - id: Secret\n    valueType: String\n    securitySensitive: true\n",
            ["records/login.yaml"] = "records:\n  - recordId: Login\n    attributes:\n      - id: ${this}/Secret\n" +
                                     $"        name: Secret\n        isOptional: true\n        access: {access}\n",
            ["enums/state.yaml"] = "enums:\n  - enumId: State\n    values:\n" + values
        }, 2, acknowledge);
    }

    private (bool Success, RecordingBuildEngine Engine) Run(string folder, bool publish = true,
        string? baseline = "Local")
    {
        var engine = new RecordingBuildEngine();
        var output = Path.Combine(_fixture.Root, "task-out", $"run{_run++}");
        var task = new CkCompile
        {
            BuildEngine = engine,
            ConstructionKitFolders = [new TaskItem(folder)],
            Compile = true,
            OutputPath = output,
            CacheFilePath = null,
            CkLinkPath = "/docs/",
            PublishCkModel = publish,
            PublishCatalogName = "LocalFileSystemCatalog",
            GenerateCkDocumentation = false,
            RefreshRemoteCatalogs = false,
            IsLocalCatalogEnabled = true,
            LocalCatalogRootPath = _catalogRoot,
            IsPublicGitHubCatalogEnabled = false,
            IsPrivateGitHubCatalogEnabled = false,
            CompatibilityBaseline = baseline
        };
        return (task.Execute(), engine);
    }

    private bool IsPublished(string modelId) =>
        Directory.EnumerateFiles(_catalogRoot, $"*{modelId.ToLowerInvariant()}*", SearchOption.AllDirectories)
            .Any(f => !f.Contains("cache"));

    private void PublishBaseline()
    {
        var (success, engine) = Run(WriteModel("2.4.0"));
        Assert.True(success, string.Join("; ", engine.Errors));
    }

    [Fact]
    public void A_breaking_change_with_a_minor_bump_fails_the_task_with_CK200_and_publishes_nothing()
    {
        PublishBaseline();

        var (success, engine) = Run(WriteModel("2.5.0", enumValues: "A"));

        Assert.False(success);
        var error = Assert.Single(engine.Errors);
        Assert.StartsWith("OCTO-CK200:", error);
        Assert.Contains("3.0.0", error);
        Assert.False(IsPublished("taskmodel-2.5.0"), "a model that fails the gate must not be published");
    }

    [Fact]
    public void The_same_change_with_a_major_bump_passes_and_is_published()
    {
        PublishBaseline();

        var (success, engine) = Run(WriteModel("3.0.0", enumValues: "A"));

        Assert.True(success, string.Join("; ", engine.Errors));
        Assert.Contains(engine.Messages, m => m.Contains("baseline TaskModel-2.4.0") && m.Contains("required level MAJOR"));
        Assert.True(IsPublished("taskmodel-3.0.0"));
    }

    [Fact]
    public void An_additive_change_passes()
    {
        PublishBaseline();

        var (success, engine) = Run(WriteModel("2.5.0", enumValues: "A,B,C"));

        Assert.True(success, string.Join("; ", engine.Errors));
        Assert.Contains(engine.Messages, m => m.StartsWith("Compatibility: model 'TaskModel'"));
    }

    [Fact]
    public void The_gate_runs_even_when_the_model_is_not_published()
    {
        PublishBaseline();

        var (success, engine) = Run(WriteModel("2.5.0", enumValues: "A"), publish: false);

        Assert.False(success);
        Assert.StartsWith("OCTO-CK200:", Assert.Single(engine.Errors));
    }

    [Fact]
    public void A_rebuild_after_a_breaking_edit_at_an_unchanged_version_still_fails()
    {
        PublishBaseline();
        Assert.True(Run(WriteModel("2.5.0", enumValues: "A,B,C")).Success);

        var (success, engine) = Run(WriteModel("2.5.0", enumValues: "A"));

        Assert.False(success);
        Assert.StartsWith("OCTO-CK200:", Assert.Single(engine.Errors));
        Assert.Contains(engine.Messages, m => m.Contains("baseline TaskModel-2.4.0"));
    }

    [Fact]
    public void The_acknowledge_path_works_inside_the_task()
    {
        PublishBaseline();

        var (missing, missingEngine) = Run(WriteModel("2.5.0", access: "Hidden"));
        var (given, givenEngine) = Run(WriteModel("2.5.0", access: "Hidden",
            acknowledge: $"compatibility:\n  acknowledge:\n    - change: \"{SecretKey}\"\n      reason: \"Close accepted risk R13\"\n"));
        var (stale, staleEngine) = Run(WriteModel("2.5.1", access: "Hidden",
            acknowledge: $"compatibility:\n  acknowledge:\n    - change: \"{SecretKey}x\"\n      reason: \"typo\"\n"));

        Assert.False(missing);
        Assert.Contains(missingEngine.Errors, e => e.StartsWith("OCTO-CK203:") && e.Contains(SecretKey));
        Assert.True(given, string.Join("; ", givenEngine.Errors));
        Assert.Contains(givenEngine.Messages, m => m.Contains("Acknowledged change:") && m.Contains("R13"));
        Assert.False(stale);
        Assert.Contains(staleEngine.Errors, e => e.StartsWith("OCTO-CK204:"));
    }

    [Fact]
    public void An_invalid_baseline_source_fails_the_task()
    {
        var (success, engine) = Run(WriteModel("2.4.0"), baseline: "Nearby");

        Assert.False(success);
        Assert.Contains("OctoCkCompatibilityBaseline", Assert.Single(engine.Errors));
    }

    [Fact]
    public void The_task_never_modifies_ckModel_yaml()
    {
        PublishBaseline();
        var folder = WriteModel("2.5.0", enumValues: "A");
        var metadata = Path.Combine(folder, "ckModel.yaml");
        var before = File.ReadAllBytes(metadata);

        Assert.False(Run(folder).Success);

        Assert.Equal(before, File.ReadAllBytes(metadata));
    }
}
