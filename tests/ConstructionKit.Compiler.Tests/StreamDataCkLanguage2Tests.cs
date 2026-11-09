using System.Text.RegularExpressions;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     AB#8597 (Feature AB#5697 F4.3, Epic AB#5584): the real System.StreamData sources (<c>src/StreamDataCkModel</c>) are
///     <c>ckLanguage: 2</c> from 1.23.0 on and a MINOR against the ckLanguage 1 release 1.22.0. Raising the language flips
///     the <c>derivable</c> default from <c>Any</c> to <c>Model</c> (SemVer row T6); the model therefore declares
///     <c>derivable: Any</c> on every type and record. The ckLanguage 1 baseline is derived from the same sources (language key
///     and explicit modifiers removed), so the test needs no stored copy of 1.22.0.
/// </summary>
public sealed class StreamDataCkLanguage2Tests : IDisposable
{
    private const string BaselineVersion = "1.22.0";

    private static readonly string[] ExpectedTypes = ["Archive", "RawArchive", "RecomputeJob", "RollupArchive", "TimeRangeArchive"];

    private static readonly string[] ExpectedRecords =
    [
        "CkArchiveColumn", "CkArchiveConflictKey", "CkArchiveDirtyWindow", "CkArchiveRecomputeRange", "CkRollupAggregation",
        "CkRollupSourceReference"
    ];

    private readonly InMemoryPublishedCatalog _published = new();
    private readonly CkCompileFixture _fixture;
    private int _outputCounter;

    public StreamDataCkLanguage2Tests()
    {
        _fixture = new CkCompileFixture(s => s.AddSingleton<ICatalog>(_published));
    }

    public void Dispose() => _fixture.Dispose();

    private static string RealSource(string project)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src", project, "ConstructionKit");
            if (File.Exists(Path.Combine(candidate, "ckModel.yaml")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException($"src/{project}/ConstructionKit not found above " + AppContext.BaseDirectory);
    }

    private static string RealStreamDataSource() => RealSource("StreamDataCkModel");

    /// <summary>Copies the real System.StreamData sources; <paramref name="transform" /> may rewrite any YAML file (relative path, text).</summary>
    private string CopyStreamData(string name, Func<string, string, string> transform)
    {
        var source = RealStreamDataSource();
        var target = Path.Combine(_fixture.Root, "src", name, "ConstructionKit");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (relative.EndsWith(".yaml", StringComparison.Ordinal))
            {
                File.WriteAllText(destination, transform(relative.Replace('\\', '/'), File.ReadAllText(file)));
            }
            else
            {
                File.Copy(file, destination);
            }
        }

        return target;
    }

    private static string SetVersion(string yaml, string version) =>
        Regex.Replace(yaml, @"modelId: ""?System\.StreamData-[\d.]+""?", $"modelId: System.StreamData-{version}");

    private static string RemoveLine(string yaml, string line) =>
        string.Join("\n", yaml.Split('\n').Where(l => l.Trim() != line));

    /// <summary>The ckLanguage 1 System.StreamData: same sources without the language key and the explicit modifiers.</summary>
    private string CopyV1StreamData(string version) =>
        CopyStreamData($"streamdata-v1-{version}", (path, yaml) => path == "ckModel.yaml"
            ? RemoveLine(SetVersion(yaml, version), "ckLanguage: 2")
            : RemoveLine(yaml, "derivable: Any"));

    /// <summary>System.StreamData depends on System; the real System goes into the fixture's local catalog first.</summary>
    private async Task PublishSystemAsync()
    {
        var system = RealSource("SystemCkModel");
        var compiled = await _fixture.CompileAsync(system);
        await _fixture.Services.GetRequiredService<ICatalogService>().PublishAsync(LocalFileSystemCatalog.Name, compiled,
            new OriginFileResolver(system), isForced: true);
    }

    private async Task<CkCompiledModelRoot> CompileStreamDataAsync(string sourceDir)
    {
        await PublishSystemAsync();
        return await _fixture.CompileAsync(sourceDir);
    }

    private async Task PublishBaselineAsync()
    {
        var baseline = CopyV1StreamData(BaselineVersion);
        var compiled = await CompileStreamDataAsync(baseline);
        Assert.True(compiled.EffectiveCkLanguage < 2);
        await _fixture.Services.GetRequiredService<ICatalogService>().PublishAsync(InMemoryPublishedCatalog.Name, compiled,
            new OriginFileResolver(baseline), isForced: true);
    }

    private async Task<CkCompileGateResult> RunGateAsync(string sourceDir)
    {
        var operationResult = new OperationResult();
        var output = Path.Combine(_fixture.Root, "out", $"run{_outputCounter++}");
        var compileResult = await _fixture.Services.GetRequiredService<ICompilerService>()
            .CompileAsync(sourceDir, output, null, operationResult);
        Assert.False(operationResult.HasErrors, string.Join(Environment.NewLine, operationResult.Messages));
        await using var stream = File.OpenRead(compileResult.CompiledModelFile);
        var compiled = await _fixture.Services.GetRequiredService<ICkSerializer>()
            .DeserializeCompiledModelRootAsync(stream, compileResult.CompiledModelFile, operationResult);
        return await _fixture.Services.GetRequiredService<CkCompileGate>().RunAsync(compiled, CkBaselineSource.Remote);
    }

    private static string[] Errors(CkCompileGateResult result) =>
        result.Messages.Where(m => m.Severity == CkCompileGateSeverity.Error).Select(m => m.Code ?? "").ToArray();

    [Fact]
    public async Task StreamData_Is_1_23_0_OnCkLanguage2_WithMinEngineVersion351_AndPublishedToTheV3RootOnly()
    {
        var compiled = await CompileStreamDataAsync(RealStreamDataSource());

        Assert.Equal("System.StreamData", compiled.ModelId.Name);
        Assert.Equal(new Version(1, 23, 0), new Version(compiled.ModelId.Version.ToString()));
        Assert.Equal(2, compiled.EffectiveCkLanguage);
        // Pinned exactly: a higher value means every service below it can no longer import System.StreamData. A feature that
        // raises it (e.g. an Archive method with execution.mode) is a deliberate rollout decision and updates this assertion.
        Assert.Equal("3.5.1", compiled.MinEngineVersion);
        Assert.True(CkCatalogLayout.RequiresV3(compiled));
        Assert.Equal(CkCatalogLayout.V3Root, CkCatalogLayout.GetPublishRoot(compiled));
    }

    [Fact]
    public async Task EveryType_AndRecord_DeclaresDerivableAny_AndStaysPublic()
    {
        var compiled = await CompileStreamDataAsync(RealStreamDataSource());

        Assert.Equal(ExpectedTypes, compiled.Types!.Select(t => t.TypeId.Name).Order());
        Assert.Equal(ExpectedRecords, compiled.Records!.Select(r => r.RecordId.Name).Order());
        Assert.All(compiled.Types!, t => Assert.True(t.Derivable == CkDerivableDto.Any, $"{t.TypeId.Name}: {t.Derivable}"));
        Assert.All(compiled.Records!, r => Assert.True(r.Derivable == CkDerivableDto.Any, $"{r.RecordId.Name}: {r.Derivable}"));
        Assert.All(compiled.Types!, t => Assert.Equal(CkVisibilityDto.Public, CkModifiers.ResolveVisibility(t.Visibility)));
        Assert.All(compiled.Records!, r => Assert.Equal(CkVisibilityDto.Public, CkModifiers.ResolveVisibility(r.Visibility)));
    }

    /// <summary>
    ///     No attribute is tightened and no interface is implemented in 1.23.0. Tightening an attribute that is not
    ///     <c>securitySensitive</c> is a MAJOR (row T7; the first-release exception T7a needs the marker), and no System.StreamData
    ///     attribute is a credential. No type carries <c>System/Name</c>, <c>System/Description</c>, <c>System/Enabled</c> or
    ///     <c>System/ParentChild</c>, so none can implement <c>Named-1</c>, <c>Describable-1</c>, <c>Enableable-1</c> or
    ///     <c>Hierarchical-1</c>. Changing either later is a deliberate, versioned decision that must update this test.
    /// </summary>
    [Fact]
    public async Task NoAttribute_IsTightenedOrSensitive_AndNoTypeImplementsAnInterface()
    {
        var compiled = await CompileStreamDataAsync(RealStreamDataSource());

        var tightened = (compiled.Types ?? [])
            .SelectMany(t => (t.Attributes ?? []).Select(a => (Owner: t.TypeId.Name, Attribute: a)))
            .Concat((compiled.Records ?? []).SelectMany(r => (r.Attributes ?? []).Select(a => (Owner: r.RecordId.Name, Attribute: a))))
            .Where(x => x.Attribute.Access is not null && x.Attribute.Access != CkAttributeAccessDto.ReadWrite)
            .Select(x => $"{x.Owner}.{x.Attribute.AttributeName}")
            .ToList();
        Assert.Empty(tightened);
        Assert.DoesNotContain(compiled.Attributes ?? [], a => a.SecuritySensitive == true);
        Assert.All(compiled.Types!, t => Assert.True(t.Implements is null || t.Implements.Count == 0, t.TypeId.Name));
    }

    [Fact]
    public async Task StreamData_1_23_0_AgainstTheCkLanguage1Release_1_22_0_IsAMinor_NotAMajor()
    {
        await PublishBaselineAsync();

        var result = await RunGateAsync(RealStreamDataSource());

        Assert.Empty(Errors(result));
        Assert.Equal(CkSemVerLevel.Minor, result.Verdict!.RequiredLevel);
        Assert.Contains(BaselineVersion, result.Verdict.BaselineId.ToString());
        Assert.DoesNotContain(result.Verdict.ClassifiedChanges, c => c.Level == CkSemVerLevel.Major);
        Assert.DoesNotContain(result.Verdict.ClassifiedChanges, c => c.RequiresAcknowledge);
    }

    [Fact]
    public async Task WithoutTheExplicitDerivableAny_TheSameSwitchWouldBeAMajor()
    {
        // Negative control: ckLanguage 2 alone flips every derivable default to Model.
        await PublishBaselineAsync();
        var trap = CopyStreamData("streamdata-trap", (path, yaml) => path == "ckModel.yaml" ? yaml : RemoveLine(yaml, "derivable: Any"));

        var result = await RunGateAsync(trap);

        Assert.Contains("OCTO-CK200", Errors(result));
        Assert.Contains(result.Verdict!.ClassifiedChanges,
            c => c.Level == CkSemVerLevel.Major && c.Change.Property == "derivable");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(2)]
    public async Task AModelOfAnotherRepository_DerivesFromArchive_AndTheRecords_WithoutMessage113(int? dependentLanguage)
    {
        var compiled = await CompileStreamDataAsync(RealStreamDataSource());
        await _fixture.Services.GetRequiredService<ICatalogService>().PublishAsync(LocalFileSystemCatalog.Name, compiled,
            new OriginFileResolver(RealStreamDataSource()), isForced: true);

        var result = new OperationResult();
        var dependent = _fixture.WriteSource($"dep-{dependentLanguage}", "Dependent-1.0.0",
            ["System-[2.5,3.0)", "System.StreamData-[1.23,2.0)"],
            new Dictionary<string, string>
            {
                ["types/t.yaml"] =
                    "types:\n  - typeId: MyArchive\n    derivedFromCkTypeId: ${System.StreamData}/Archive\n    isAbstract: true\n",
                ["records/r.yaml"] =
                    "records:\n  - recordId: MyColumn\n    derivedFromCkRecordId: ${System.StreamData}/CkArchiveColumn\n"
            }, dependentLanguage);

        await _fixture.Services.GetRequiredService<ICompilerService>().CompileInMemoryAsync(dependent, result);

        Assert.DoesNotContain(result.Messages, m => m.MessageNumber == 113);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Messages));
    }
}
