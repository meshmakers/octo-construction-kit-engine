using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Templates;

/// <summary>
///     AB#4418: the packed <c>ConstructionKit</c> dotnet-new template must reference the sibling packages at the
///     pack version of the template package itself (never a hardcoded <c>999.0.0.0</c>), so that a scaffold
///     restores from the feed the template came from. The tests really run <c>dotnet pack</c> on the Templates
///     project into a scratch folder, open the nupkg and assert the scaffold csproj.
/// </summary>
public class TemplatePackVersionTests : IDisposable
{
    private const string TemplatePackageId = "Meshmakers.Octo.ConstructionKit.Templates";
    private const string ScaffoldEntry = "content/Templates/ConstructionKit.CSharp/GeneratedConstructionKitProject.csproj";

    private static readonly string[] ReferencedPackages =
    [
        "Meshmakers.Octo.Runtime.Contracts",
        "Meshmakers.Octo.ConstructionKit.SourceGeneration",
        "Meshmakers.Octo.ConstructionKit.MsBuildTasks"
    ];

    private readonly string _outputDirectory =
        Path.Combine(Path.GetTempPath(), "octo-template-pack-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void SourceScaffold_UsesPlaceholder_NotAHardcodedVersion()
    {
        var content = File.ReadAllText(Path.Combine(TemplateProjectDirectory(),
            "Templates", "ConstructionKit.CSharp", "GeneratedConstructionKitProject.csproj"));

        foreach (var package in ReferencedPackages)
        {
            Assert.Matches(new Regex($"Include=\"{Regex.Escape(package)}\"[^>]*Version=\"__OCTO_TEMPLATE_PACKAGE_VERSION__\""),
                content);
        }

        Assert.DoesNotContain("999.0.0", content);
    }

    [Theory]
    [InlineData("7.8.9")]
    [InlineData("0.1.2610.9010")]
    [InlineData("3.5.1-beta.2")]
    public void Pack_WithPackageVersion_ScaffoldReferencesThatVersion(string version)
    {
        var scaffold = PackAndReadScaffold($"-p:PackageVersion={version}", version);

        AssertReferences(scaffold, version);
    }

    [Fact]
    public void Pack_DebugL_ScaffoldReferences999()
    {
        var scaffold = PackAndReadScaffold(null, "999.0.0");

        AssertReferences(scaffold, "999.0.0");
    }

    private static void AssertReferences(string scaffold, string version)
    {
        Assert.DoesNotContain("__OCTO_TEMPLATE_PACKAGE_VERSION__", scaffold);
        Assert.DoesNotContain("999.0.0.0", scaffold);

        foreach (var package in ReferencedPackages)
        {
            Assert.Matches(new Regex($"Include=\"{Regex.Escape(package)}\"[^>]*Version=\"{Regex.Escape(version)}\""),
                scaffold);
        }

        // The rest of the scaffold must survive the replacement unchanged (also on Linux/macOS).
        Assert.Contains("Include=\"$(MSBuildProjectDirectory)/ConstructionKit\"", scaffold);
    }

    private string PackAndReadScaffold(string? versionArgument, string expectedPackageVersion)
    {
        var projectFile = Path.Combine(TemplateProjectDirectory(), "ConstructionKit.Templates.csproj");
        Directory.CreateDirectory(_outputDirectory);

        var arguments = $"pack \"{projectFile}\" -c DebugL -p:NuGetAudit=false --nologo -o \"{_outputDirectory}\"";
        if (versionArgument != null)
        {
            arguments += " " + versionArgument;
        }

        RunDotnet(arguments);

        var nupkg = Path.Combine(_outputDirectory, $"{TemplatePackageId}.{expectedPackageVersion}.nupkg");
        Assert.True(File.Exists(nupkg), $"Expected package {nupkg}; found: {string.Join(", ", Directory.GetFiles(_outputDirectory))}");

        using var archive = ZipFile.OpenRead(nupkg);
        var entry = archive.GetEntry(ScaffoldEntry);
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static void RunDotnet(string arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet", arguments)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(3)))
        {
            process.Kill(true);
            Assert.Fail("dotnet pack timed out");
        }

        Assert.True(process.ExitCode == 0, $"dotnet {arguments} failed:{Environment.NewLine}{stdout.Result}{stderr.Result}");
    }

    private static string TemplateProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Octo.ConstructionKit.Engine.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "src", "ConstructionKit.Templates");
    }
}
