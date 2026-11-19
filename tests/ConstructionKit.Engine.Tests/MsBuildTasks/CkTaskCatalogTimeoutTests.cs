using System.Collections;
using Meshmakers.Octo.ConstructionKit.Engine.Tests.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.MsBuildTasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.MsBuildTasks;

/// <summary>
///     AB#9388: the REAL CkCompile and CkRestore tasks with the private catalog pointing at a server that accepts the
///     connection and never answers. Their time limit cancels the catalog refresh through its token, and the task fails
///     with the time-limit error instead of blocking (before the fix the request ran on after the limit).
/// </summary>
public sealed class CkTaskCatalogTimeoutTests : IDisposable
{
    private static readonly TimeSpan TaskTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan Bounded = TimeSpan.FromSeconds(20);

    private readonly SilentTcpServer _server = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CkTaskCatalogTimeoutTests", Guid.NewGuid().ToString());

    public CkTaskCatalogTimeoutTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "ConstructionKit"));
        File.WriteAllText(Path.Combine(_root, "ConstructionKit", "ckModel.yaml"),
            "modelId: \"TimeoutModel-1.0.0\"\ndescription: \"AB#9388\"\ndependencies:\n  - \"System-[2.0,3.0)\"\n");
        Directory.CreateDirectory(Path.Combine(_root, "catalog"));
    }

    public void Dispose()
    {
        _server.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void CkCompile_cancels_the_hanging_catalog_refresh_at_its_time_limit()
    {
        var engine = new RecordingBuildEngine();
        var task = new CkCompile
        {
            BuildEngine = engine,
            ConstructionKitFolders = [new TaskItem(Path.Combine(_root, "ConstructionKit"))],
            Compile = true,
            OutputPath = Path.Combine(_root, "out"),
            CkLinkPath = "/docs/",
            PublishCkModel = false,
            GenerateCkDocumentation = false,
            RefreshRemoteCatalogs = true,
            IsLocalCatalogEnabled = true,
            LocalCatalogRootPath = Path.Combine(_root, "catalog"),
            IsPublicGitHubCatalogEnabled = false,
            IsPrivateGitHubCatalogEnabled = true,
            PrivateGitHubCatalogOwner = "testowner",
            PrivateGitHubCatalogRepositoryName = "testrepo",
            PrivateGitHubCatalogBranch = "main",
            PrivateGitHubCatalogPagesUri = _server.BaseUri.ToString(),
            CompatibilityBaseline = "Local",
            TaskTimeout = TaskTimeout
        };

        AssertFailsWithTimeLimit(task.Execute, engine, "compile");
    }

    [Fact]
    public void CkRestore_cancels_the_hanging_catalog_refresh_at_its_time_limit()
    {
        var engine = new RecordingBuildEngine();
        var task = new CkRestore
        {
            BuildEngine = engine,
            ConstructionKitModelConfigFiles = [new TaskItem(Path.Combine(_root, "ConstructionKit", "ckModel.yaml"))],
            OutputPath = Path.Combine(_root, "out"),
            CacheFilePath = Path.Combine(_root, "cache"),
            IsLocalCatalogEnabled = true,
            LocalCatalogRootPath = Path.Combine(_root, "catalog"),
            IsPublicGitHubCatalogEnabled = false,
            IsPrivateGitHubCatalogEnabled = true,
            PrivateGitHubCatalogOwner = "testowner",
            PrivateGitHubCatalogRepositoryName = "testrepo",
            PrivateGitHubCatalogBranch = "main",
            PrivateGitHubCatalogPagesUri = _server.BaseUri.ToString(),
            TaskTimeout = TaskTimeout
        };

        AssertFailsWithTimeLimit(task.Execute, engine, "restore");
    }

    private void AssertFailsWithTimeLimit(Func<bool> execute, RecordingBuildEngine engine, string operation)
    {
        // Execute runs under its own bound, so a regression fails the test instead of hanging it.
        var run = System.Threading.Tasks.Task.Run(execute, TestContext.Current.CancellationToken);
        Assert.True(run.Wait(Bounded, TestContext.Current.CancellationToken), $"Execute did not return within {Bounded}");

        Assert.False(run.Result);
        var error = Assert.Single(engine.Errors);
        Assert.Contains($"Construction kit {operation} did not complete within", error);
        Assert.Contains("was canceled", error);
        Assert.True(_server.AcceptedConnections >= 1, "the refresh must have reached the silent catalog server");
    }

    private sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<string> Errors { get; } = [];
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => "test.csproj";

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e.Message ?? "");
        public void LogWarningEvent(BuildWarningEventArgs e) { }
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogCustomEvent(CustomBuildEventArgs e) { }

        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties,
            IDictionary targetOutputs) => true;
    }
}
