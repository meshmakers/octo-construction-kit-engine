using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.MsBuildTasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Xunit;

namespace ConstructionKit.Engine.Tests.MsBuildTasks;

/// <summary>
///     Unit tests for the Phase-2 <see cref="CkLintRuntimeStateMarkers"/> MSBuild task.
///     Each test writes real YAML files to a temp folder so the deserialiser exercises
///     the same parsing path it sees at build time, then captures
///     <see cref="IBuildEngine.LogErrorEvent"/> calls via FakeItEasy to assert what was
///     reported.
/// </summary>
public sealed class CkLintRuntimeStateMarkersTests : IDisposable
{
    private readonly string _root;
    private readonly List<BuildErrorEventArgs> _errors = new();

    public CkLintRuntimeStateMarkersTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ck-lint-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Best-effort temp cleanup — tolerate failures so we don't mask the test result.
        }
    }

    [Fact]
    public void Execute_AllAttributesMarked_ReturnsTrue()
    {
        var ckFolder = CreateCkFolder("ConstructionKit");
        WriteAttributesYaml(ckFolder, "core.yaml",
            ("Status", isRuntimeState: true),
            ("Name", isRuntimeState: false));

        var task = MakeTask(ckFolder);

        Assert.True(task.Execute());
        Assert.Empty(_errors);
    }

    [Fact]
    public void Execute_OneAttributeUnmarked_ReportsErrorAndReturnsFalse()
    {
        var ckFolder = CreateCkFolder("ConstructionKit");
        WriteAttributesYaml(ckFolder, "core.yaml",
            ("Status", isRuntimeState: true),
            ("Lonely", isRuntimeState: null));

        var task = MakeTask(ckFolder);

        Assert.False(task.Execute());
        var error = Assert.Single(_errors);
        Assert.Equal("OCTO-CK001", error.Code);
        Assert.Contains("Lonely", error.Message ?? string.Empty);
        Assert.EndsWith("core.yaml", error.File ?? string.Empty);
    }

    [Fact]
    public void Execute_MultipleUnmarkedAttributes_ReportsAllAndReturnsFalse()
    {
        var ckFolder = CreateCkFolder("ConstructionKit");
        WriteAttributesYaml(ckFolder, "core.yaml",
            ("A", isRuntimeState: null),
            ("B", isRuntimeState: true),
            ("C", isRuntimeState: null));

        var task = MakeTask(ckFolder);

        Assert.False(task.Execute());
        Assert.Equal(2, _errors.Count);
        Assert.Contains(_errors, e => (e.Message ?? string.Empty).Contains("'A'"));
        Assert.Contains(_errors, e => (e.Message ?? string.Empty).Contains("'C'"));
        Assert.DoesNotContain(_errors, e => (e.Message ?? string.Empty).Contains("'B'"));
    }

    [Fact]
    public void Execute_FolderWithoutAttributesDir_NoOps()
    {
        var ckFolder = CreateCkFolder("ConstructionKit");
        // Deliberately no 'attributes' subfolder.

        var task = MakeTask(ckFolder);

        Assert.True(task.Execute());
        Assert.Empty(_errors);
    }

    [Fact]
    public void Execute_MultipleConstructionKitFolders_ScansAll()
    {
        var first = CreateCkFolder("First");
        var second = CreateCkFolder("Second");
        WriteAttributesYaml(first, "core.yaml", ("FirstAttr", isRuntimeState: true));
        WriteAttributesYaml(second, "core.yaml", ("SecondAttr", isRuntimeState: null));

        var task = MakeTask(first, second);

        Assert.False(task.Execute());
        var error = Assert.Single(_errors);
        Assert.Contains("SecondAttr", error.Message ?? string.Empty);
    }

    [Fact]
    public void Execute_MalformedYaml_ReportsParseErrorAndReturnsFalse()
    {
        var ckFolder = CreateCkFolder("ConstructionKit");
        var attributesDir = Path.Combine(ckFolder, "attributes");
        Directory.CreateDirectory(attributesDir);
        File.WriteAllText(Path.Combine(attributesDir, "broken.yaml"),
            "this: is: not: valid: yaml: [unterminated");

        var task = MakeTask(ckFolder);

        Assert.False(task.Execute());
        var error = Assert.Single(_errors);
        Assert.Equal("OCTO-CK002", error.Code);
    }

    [Fact]
    public void Execute_EmptyAttributesList_NoErrors()
    {
        var ckFolder = CreateCkFolder("ConstructionKit");
        var attributesDir = Path.Combine(ckFolder, "attributes");
        Directory.CreateDirectory(attributesDir);
        // attributes: key present but list is empty — valid YAML, nothing to lint.
        File.WriteAllText(Path.Combine(attributesDir, "empty.yaml"), "attributes: []\n");

        var task = MakeTask(ckFolder);

        Assert.True(task.Execute());
        Assert.Empty(_errors);
    }

    // ----- AB#5187: the ownership marker -----

    [Theory]
    [InlineData("SeedOwned")]
    [InlineData("TenantOwned")]
    [InlineData("RuntimeState")]
    [InlineData("Secret")]
    public void Execute_OwnershipMarker_SatisfiesTheLint(string ownership)
    {
        var ckFolder = CreateCkFolder("ConstructionKit");
        WriteAttributesRaw(ckFolder, "core.yaml",
            ("Marked", $"  ownership: {ownership}"));

        var task = MakeTask(ckFolder);

        Assert.True(task.Execute());
        Assert.Empty(_errors);
    }

    [Fact]
    public void Execute_LegacyMarkerStillSatisfiesTheLint_AlongsideOwnership()
    {
        // A project mid-migration has both styles in the same folder; neither may fail.
        var ckFolder = CreateCkFolder("ConstructionKit");
        WriteAttributesRaw(ckFolder, "mixed.yaml",
            ("Legacy", "  isRuntimeState: true"),
            ("LegacyFalse", "  isRuntimeState: false"),
            ("Modern", "  ownership: Secret"));

        var task = MakeTask(ckFolder);

        Assert.True(task.Execute());
        Assert.Empty(_errors);
    }

    [Fact]
    public void Execute_NeitherMarker_ReportsOCTOCK001MentioningOwnership()
    {
        var ckFolder = CreateCkFolder("ConstructionKit");
        WriteAttributesRaw(ckFolder, "core.yaml", ("Lonely", null));

        var task = MakeTask(ckFolder);

        Assert.False(task.Execute());
        var error = Assert.Single(_errors);
        Assert.Equal("OCTO-CK001", error.Code);
        Assert.Contains("Lonely", error.Message ?? string.Empty);
        Assert.Contains("ownership", error.Message ?? string.Empty);
    }

    [Fact]
    public void Execute_BothMarkersOnOneAttribute_ReportsOCTOCK003()
    {
        // The engine resolves this deterministically (ownership wins), but a stale alias that
        // contradicts the enum is exactly how a credential ends up unprotected — so it is rejected.
        var ckFolder = CreateCkFolder("ConstructionKit");
        WriteAttributesRaw(ckFolder, "core.yaml",
            ("Conflicted", "  ownership: Secret\n  isRuntimeState: false"));

        var task = MakeTask(ckFolder);

        Assert.False(task.Execute());
        var error = Assert.Single(_errors);
        Assert.Equal("OCTO-CK003", error.Code);
        Assert.Contains("Conflicted", error.Message ?? string.Empty);
    }

    [Fact]
    public void Execute_UnknownOwnershipValue_ReportsOCTOCK004WithTheValidValues()
    {
        var ckFolder = CreateCkFolder("ConstructionKit");
        WriteAttributesRaw(ckFolder, "core.yaml", ("Typo", "  ownership: TenantOwn"));

        var task = MakeTask(ckFolder);

        Assert.False(task.Execute());
        var error = Assert.Single(_errors);
        Assert.Equal("OCTO-CK004", error.Code);
        Assert.Contains("TenantOwn", error.Message ?? string.Empty);
        Assert.Contains("TenantOwned", error.Message ?? string.Empty);
    }

    // ----- helpers -----

    private string CreateCkFolder(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteAttributesYaml(
        string ckFolder, string fileName,
        params (string id, bool? isRuntimeState)[] entries)
    {
        var attributesDir = Path.Combine(ckFolder, "attributes");
        Directory.CreateDirectory(attributesDir);

        using var writer = File.CreateText(Path.Combine(attributesDir, fileName));
        writer.WriteLine("attributes:");
        foreach (var (id, isRuntimeState) in entries)
        {
            writer.WriteLine($"- id: {id}");
            writer.WriteLine("  valueType: String");
            if (isRuntimeState.HasValue)
            {
                writer.WriteLine($"  isRuntimeState: {(isRuntimeState.Value ? "true" : "false")}");
            }
        }
    }

    /// <summary>
    ///     Writes attribute entries with an arbitrary extra YAML line (or none), so a test can
    ///     express marker combinations the typed helper above cannot — both markers at once, an
    ///     invalid enum value, or no marker at all.
    /// </summary>
    private static void WriteAttributesRaw(string ckFolder, string fileName,
        params (string id, string? extraLines)[] entries)
    {
        var attributesDir = Path.Combine(ckFolder, "attributes");
        Directory.CreateDirectory(attributesDir);

        using var writer = File.CreateText(Path.Combine(attributesDir, fileName));
        writer.WriteLine("attributes:");
        foreach (var (id, extraLines) in entries)
        {
            writer.WriteLine($"- id: {id}");
            writer.WriteLine("  valueType: String");
            if (extraLines != null)
            {
                writer.WriteLine(extraLines);
            }
        }
    }

    private CkLintRuntimeStateMarkers MakeTask(params string[] ckFolders)
    {
        var items = new ITaskItem[ckFolders.Length];
        for (var i = 0; i < ckFolders.Length; i++)
        {
            items[i] = new TaskItem(ckFolders[i]);
        }

        var engine = A.Fake<IBuildEngine>();
        A.CallTo(() => engine.LogErrorEvent(A<BuildErrorEventArgs>._))
            .Invokes(call => _errors.Add(call.GetArgument<BuildErrorEventArgs>(0)!));

        return new CkLintRuntimeStateMarkers
        {
            ConstructionKitFolders = items,
            BuildEngine = engine
        };
    }
}
