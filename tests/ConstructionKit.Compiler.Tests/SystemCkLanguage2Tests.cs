using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     AB#6928 (Epic AB#5584 F4.1): the real System sources (<c>src/SystemCkModel</c>) are <c>ckLanguage: 2</c> and a
///     MINOR against the ckLanguage 1 System 2.5.0. Raising the language flips the <c>derivable</c> default from
///     <c>Any</c> to <c>Model</c>; the System therefore declares <c>derivable: Any</c> on every type and record, or
///     every model deriving from <c>System/Entity</c> would break (message 113, SemVer row T6). The v1 baseline is
///     derived from the same sources (language key and explicit modifiers removed), so the test needs no stored copy.
/// </summary>
public sealed class SystemCkLanguage2Tests : IDisposable
{
    private readonly InMemoryPublishedCatalog _published = new();
    private readonly CkCompileFixture _fixture;
    private int _outputCounter;

    public SystemCkLanguage2Tests()
    {
        _fixture = new CkCompileFixture(s => s.AddSingleton<ICatalog>(_published));
    }

    public void Dispose() => _fixture.Dispose();

    private static string RealSystemSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "SystemCkModel", "ConstructionKit");
            if (File.Exists(Path.Combine(candidate, "ckModel.yaml")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("src/SystemCkModel/ConstructionKit not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Copies the real System sources; <paramref name="transform" /> may rewrite any file (relative path, text).</summary>
    private string CopySystem(string name, Func<string, string, string> transform)
    {
        var source = RealSystemSource();
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
        System.Text.RegularExpressions.Regex.Replace(yaml, @"modelId: ""System-[\d.]+""", $"modelId: \"System-{version}\"");

    private static string RemoveLine(string yaml, string line) =>
        string.Join("\n", yaml.Split('\n').Where(l => l.Trim() != line));

    /// <summary>The ckLanguage 1 System: same sources without the language key and without the explicit modifiers.</summary>
    private string CopyV1System(string version) => CopySystem($"system-v1-{version}", (path, yaml) =>
        path == "ckModel.yaml"
            ? RemoveLine(SetVersion(yaml, version), "ckLanguage: 2")
            : RemoveLine(yaml, "derivable: Any"));

    private async Task<CkCompiledModelRoot> CompileToFileAndBackAsync(string sourceDir)
    {
        var operationResult = new OperationResult();
        var output = Path.Combine(_fixture.Root, "out", $"run{_outputCounter++}");
        var compileResult = await _fixture.Services.GetRequiredService<ICompilerService>()
            .CompileAsync(sourceDir, output, null, operationResult);
        Assert.False(operationResult.HasErrors, string.Join(Environment.NewLine, operationResult.Messages));
        await using var stream = File.OpenRead(compileResult.CompiledModelFile);
        return await _fixture.Services.GetRequiredService<ICkSerializer>()
            .DeserializeCompiledModelRootAsync(stream, compileResult.CompiledModelFile, operationResult);
    }

    private async Task PublishAsync(string sourceDir, string catalog)
    {
        var compiled = await _fixture.CompileAsync(sourceDir);
        await _fixture.Services.GetRequiredService<ICatalogService>().PublishAsync(catalog, compiled,
            new OriginFileResolver(sourceDir), isForced: true);
    }

    private async Task<CkCompileGateResult> RunGateAsync(string sourceDir) =>
        await _fixture.Services.GetRequiredService<CkCompileGate>()
            .RunAsync(await CompileToFileAndBackAsync(sourceDir), CkBaselineSource.Remote);

    private static string[] Errors(CkCompileGateResult result) =>
        result.Messages.Where(m => m.Severity == CkCompileGateSeverity.Error).Select(m => m.Code ?? "").ToArray();

    [Fact]
    public async Task System_IsCkLanguage2_WithMinEngineVersion351_AndPublishedToTheV3RootOnly()
    {
        var compiled = await _fixture.CompileAsync(RealSystemSource());

        Assert.Equal(2, compiled.EffectiveCkLanguage);
        Assert.Equal("3.5.1", compiled.MinEngineVersion);
        Assert.True(CkCatalogLayout.RequiresV3(compiled));
        Assert.Equal(CkCatalogLayout.V3Root, CkCatalogLayout.GetPublishRoot(compiled));
        Assert.Equal("System", compiled.ModelId.Name);
        Assert.Equal(new Version(2, 6, 0), new Version(compiled.ModelId.Version.ToString()));
    }

    [Fact]
    public async Task EveryType_AndRecord_DeclaresDerivableAny_SoNoDerivationIsTightened()
    {
        var compiled = await _fixture.CompileAsync(RealSystemSource());

        Assert.NotEmpty(compiled.Types!);
        Assert.NotEmpty(compiled.Records!);
        Assert.All(compiled.Types!, t => Assert.True(t.Derivable == CkDerivableDto.Any, $"{t.TypeId.Name}: {t.Derivable}"));
        Assert.All(compiled.Records!, r => Assert.True(r.Derivable == CkDerivableDto.Any, $"{r.RecordId.Name}: {r.Derivable}"));
        // Nothing else was tightened either: no internal element, no final type that was not final before.
        Assert.All(compiled.Types!, t => Assert.Equal(CkVisibilityDto.Public, CkModifiers.ResolveVisibility(t.Visibility)));
        Assert.All(compiled.Records!, r => Assert.Equal(CkVisibilityDto.Public, CkModifiers.ResolveVisibility(r.Visibility)));
    }

    [Fact]
    public async Task EveryPreExistingElement_OfTheV1System_IsAlsoDerivableAnyInTheV2System()
    {
        // The v1 baseline resolves derivable to Any by default; the v2 System must resolve to the same for every element.
        var v1 = await _fixture.CompileAsync(CopyV1System("2.5.0"));
        var v2 = await _fixture.CompileAsync(RealSystemSource());

        Assert.Equal(v1.Types!.Select(t => t.TypeId.Name).Order(), v2.Types!.Select(t => t.TypeId.Name).Order());
        Assert.Equal(v1.Records!.Select(r => r.RecordId.Name).Order(), v2.Records!.Select(r => r.RecordId.Name).Order());
        foreach (var t in v1.Types!)
        {
            Assert.Equal(CkModifiers.ResolveDerivable(t.Derivable, v1.EffectiveCkLanguage),
                CkModifiers.ResolveDerivable(v2.Types!.Single(x => x.TypeId.Name == t.TypeId.Name).Derivable,
                    v2.EffectiveCkLanguage));
        }
    }

    [Fact]
    public async Task ATypeNewInTheModel_WithoutDerivable_DefaultsToModel_AndCannotBeDerivedFromElsewhere()
    {
        // The probe is a temporary copy of the sources; the real sources are never touched.
        var probe = CopySystem("system-probe", (_, yaml) => yaml);
        File.WriteAllText(Path.Combine(probe, "types", "probe.yaml"),
            "types:\n  - typeId: Probe\n    derivedFromCkTypeId: ${this}/Entity\n    isAbstract: true\n");
        var compiled = await _fixture.CompileAsync(probe);
        var probeType = compiled.Types!.Single(t => t.TypeId.Name == "Probe");

        Assert.Null(probeType.Derivable);
        Assert.Equal(CkDerivableDto.Model, CkModifiers.ResolveDerivable(probeType.Derivable, compiled.EffectiveCkLanguage));

        await PublishAsync(probe, LocalFileSystemCatalog.Name);
        var result = new OperationResult();
        var dependent = _fixture.WriteSource("dep-probe", "Dependent-1.0.0", ["System-[2.6,3.0)"],
            new Dictionary<string, string>
            {
                ["types/t.yaml"] = "types:\n  - typeId: Mine\n    derivedFromCkTypeId: ${System}/Probe\n"
            });
        try
        {
            await _fixture.Services.GetRequiredService<ICompilerService>().CompileInMemoryAsync(dependent, result);
        }
        catch (Exception)
        {
            // asserted through the messages
        }

        Assert.Contains(result.Messages, m => m.MessageNumber == 113 && m.MessageText.Contains("Probe"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(2)]
    public async Task AModelOfAnotherRepository_DerivesFromTheStableBases_WithoutMessage113(int? dependentLanguage)
    {
        await PublishAsync(RealSystemSource(), LocalFileSystemCatalog.Name);
        var result = new OperationResult();
        var dependent = _fixture.WriteSource($"dep-{dependentLanguage}", "Dependent-1.0.0", ["System-[2.6,3.0)"],
            new Dictionary<string, string>
            {
                ["types/t.yaml"] =
                    "types:\n" +
                    "  - typeId: MyEntity\n    derivedFromCkTypeId: ${System}/Entity\n" +
                    "  - typeId: MyConfiguration\n    derivedFromCkTypeId: ${System}/Configuration\n    isAbstract: false\n" +
                    "  - typeId: MyQuery\n    derivedFromCkTypeId: ${System}/SimpleRtQuery\n" +
                    "  - typeId: MyStreamQuery\n    derivedFromCkTypeId: ${System}/StreamDataQuery\n",
                ["records/r.yaml"] =
                    "records:\n  - recordId: MyFilter\n    derivedFromCkRecordId: ${System}/FieldFilter\n"
            }, dependentLanguage);

        await _fixture.Services.GetRequiredService<ICompilerService>().CompileInMemoryAsync(dependent, result);

        Assert.DoesNotContain(result.Messages, m => m.MessageNumber == 113);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Messages));
    }

    [Fact]
    public async Task System26_AgainstSystem250_IsAMinor_NotAMajor()
    {
        await PublishAsync(CopyV1System("2.5.0"), InMemoryPublishedCatalog.Name);

        var result = await RunGateAsync(RealSystemSource());

        Assert.Empty(Errors(result));
        Assert.Equal(CkSemVerLevel.Minor, result.Verdict!.RequiredLevel);
        Assert.Contains("2.5.0", result.Verdict.BaselineId.ToString());
        // Only the language switch itself is reported, no element is tightened.
        Assert.DoesNotContain(result.Verdict.ClassifiedChanges, c => c.Level == CkSemVerLevel.Major);
    }

    [Fact]
    public async Task WithoutTheExplicitDerivableAny_TheSameSwitchWouldBeAMajor()
    {
        // Negative control for the trap of AB#6928: ckLanguage 2 alone flips every derivable default to Model.
        await PublishAsync(CopyV1System("2.5.0"), InMemoryPublishedCatalog.Name);
        var trap = CopySystem("system-trap", (path, yaml) => path == "ckModel.yaml" ? yaml : RemoveLine(yaml, "derivable: Any"));

        var result = await RunGateAsync(trap);

        Assert.Contains("OCTO-CK200", Errors(result));
        Assert.Contains(result.Verdict!.ClassifiedChanges,
            c => c.Level == CkSemVerLevel.Major && c.Change.Property == "derivable");
    }

    [Fact]
    public async Task AUniqueIndexOnEntity_IsAMajorWithAcknowledgement_BecauseEntityIsAStableBaseByDeclaration()
    {
        // Row B4 against the real v2 System (not the v1 name branch of the classifier): System 2.6.0 is the baseline,
        // a unique index on System/Entity in 2.7.0 is Major and needs an acknowledgement.
        await PublishAsync(RealSystemSource(), InMemoryPublishedCatalog.Name);
        var next = CopySystem("system-2.7.0", (path, yaml) =>
        {
            if (path == "ckModel.yaml")
            {
                return SetVersion(yaml, "2.7.0");
            }

            return path == "types/entity.yaml"
                ? yaml.Replace("    indexes:\n",
                    "    indexes:\n      - indexType: \"UniqueNotDeleted\"\n        fields:\n          - attributePaths:\n              - RtBlueprintSource\n",
                    StringComparison.Ordinal)
                : yaml;
        });

        var result = await RunGateAsync(next);

        Assert.Contains("OCTO-CK200", Errors(result));
        Assert.Contains("OCTO-CK203", Errors(result));
        Assert.Contains(result.Verdict!.ClassifiedChanges, c => c.RequiresAcknowledge && c.Level == CkSemVerLevel.Major);
    }
}
