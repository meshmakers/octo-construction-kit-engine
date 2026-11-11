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

    /// <summary>The records of 1.22.0: each declares <c>derivable: Any</c>.</summary>
    private static readonly string[] PreExistingRecords =
    [
        "CkArchiveColumn", "CkArchiveConflictKey", "CkArchiveDirtyWindow", "CkArchiveRecomputeRange", "CkRollupAggregation",
        "CkRollupSourceReference"
    ];

    /// <summary>
    ///     The parameter and result records of the archive lifecycle methods (AB#8598, 1.24.0). New records keep the ckLanguage 2
    ///     default <c>derivable: Model</c>: they are method inputs and answers, not extension points.
    /// </summary>
    internal static readonly string[] MethodRecords =
        ["ArchiveColumnSpec", "ArchiveTransitionResult", "RecomputeJobInfo", "RollupArchiveCreateInput", "TimeRangeArchiveCreateInput"];

    /// <summary>The files that hold only the method records and their attributes (AB#8598).</summary>
    internal static readonly string[] MethodOnlyFiles = ["records/archiveMethodRecords.yaml", "attributes/archiveMethodAttributes.yaml"];

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

    /// <summary>
    ///     Removes every <c>methods:</c> block (the block ends at the next line that is not deeper indented). Methods need
    ///     <c>ckLanguage: 2</c>, so a ckLanguage 1 copy of the sources must not carry them (AB#8598).
    /// </summary>
    internal static string RemoveMethods(string yaml)
    {
        var result = new List<string>();
        int? blockIndent = null;
        foreach (var line in yaml.Split('\n'))
        {
            var indent = line.Length - line.TrimStart().Length;
            if (blockIndent is { } open)
            {
                if (line.Trim().Length == 0 || indent > open)
                {
                    continue;
                }

                blockIndent = null;
            }

            if (line.Trim() == "methods:")
            {
                blockIndent = indent;
                continue;
            }

            result.Add(line);
        }

        return string.Join("\n", result);
    }

    /// <summary>
    ///     The ckLanguage 1 System.StreamData: same sources without the language key, the explicit modifiers and the CK methods
    ///     with their records (methods exist only in ckLanguage 2).
    /// </summary>
    private string CopyV1StreamData(string version) =>
        CopyStreamData($"streamdata-v1-{version}", (path, yaml) => path == "ckModel.yaml"
            ? RemoveLine(SetVersion(yaml, version), "ckLanguage: 2")
            : MethodOnlyFiles.Contains(path)
                ? RemoveElementsOf(yaml)
                : RemoveMethods(RemoveLine(yaml, "derivable: Any")));

    /// <summary>Keeps only the schema line of a method-only file, so the copy holds none of its elements.</summary>
    internal static string RemoveElementsOf(string yaml) =>
        yaml.Split('\n')[0] + (yaml.Contains("\nrecords:", StringComparison.Ordinal) ? "\nrecords: []\n" : "\nattributes: []\n");

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
    public async Task StreamData_Is_1_25_0_OnCkLanguage2_WithMinEngineVersion351_AndPublishedToTheV3RootOnly()
    {
        var compiled = await CompileStreamDataAsync(RealStreamDataSource());

        Assert.Equal("System.StreamData", compiled.ModelId.Name);
        // 1.23.0 (AB#8597) raised the language; 1.24.0 (AB#8598) adds the archive lifecycle methods, 1.25.0 (AB#8599) the
        // rollup control methods.
        Assert.Equal(new Version(1, 25, 0), new Version(compiled.ModelId.Version.ToString()));
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
        Assert.Equal(PreExistingRecords.Concat(MethodRecords).Order(StringComparer.Ordinal),
            compiled.Records!.Select(r => r.RecordId.Name).Order(StringComparer.Ordinal));
        Assert.All(compiled.Types!, t => Assert.True(t.Derivable == CkDerivableDto.Any, $"{t.TypeId.Name}: {t.Derivable}"));
        Assert.All(compiled.Records!.Where(r => PreExistingRecords.Contains(r.RecordId.Name)),
            r => Assert.True(r.Derivable == CkDerivableDto.Any, $"{r.RecordId.Name}: {r.Derivable}"));
        Assert.All(compiled.Records!.Where(r => MethodRecords.Contains(r.RecordId.Name)),
            r => Assert.Equal(CkDerivableDto.Model, CkModifiers.ResolveDerivable(r.Derivable, 2)));
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
    public async Task StreamData_AgainstTheCkLanguage1Release_1_22_0_IsAMinor_NotAMajor()
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

    // ---------- AB#8598 (1.24.0): archive lifecycle methods -------------------------------------------------------------

    /// <summary>The error codes of the streamData mutations (asset repository) plus STREAMDATA_NOT_ENABLED, on every method.</summary>
    private static readonly string[] ArchiveMethodErrorCodes =
    [
        "STREAMDATA_ACTIVATION_FAILED", "STREAMDATA_ARCHIVE_NOT_ACTIVATED", "STREAMDATA_ARCHIVE_NOT_FOUND", "STREAMDATA_ERROR",
        "STREAMDATA_INVALID_TRANSITION", "STREAMDATA_NOT_ENABLED", "STREAMDATA_PATH_INVALID", "STREAMDATA_SCHEMA_IMMUTABLE"
    ];

    /// <summary>
    ///     AB#8598: the seven streamData mutations have a CK method each. The lifecycle methods sit on the abstract
    ///     <c>Archive</c> (every concrete archive inherits them), the create methods are static on the two archive types that have
    ///     a create mutation. Role StreamDataAdmin (the role the mutations check), every STREAMDATA_* code of the mutations, an
    ///     explicit timeout, and no replacesGenericCreate (blueprints seed archives through ImportRt).
    /// </summary>
    [Fact]
    public async Task ArchiveLifecycleMethods_CarryTheContractOfTheMutations()
    {
        var compiled = await CompileStreamDataAsync(RealStreamDataSource());
        var types = compiled.Types!.ToDictionary(t => t.TypeId.Name);

        var archive = types["Archive"].Methods!.ToDictionary(m => m.MethodId);
        Assert.Equal(["Activate-1", "DeleteArchive-1", "Disable-1", "Enable-1", "RetryActivation-1"],
            archive.Keys.Order(StringComparer.Ordinal));
        foreach (var id in new[] { "Activate-1", "Disable-1", "Enable-1", "RetryActivation-1" })
        {
            Assert.Equal(CkMethodKindDto.Instance, archive[id].Kind);
            Assert.Empty(archive[id].Parameters ?? []);
            Assert.Equal(AttributeValueTypesDto.Record, archive[id].Result!.ValueType);
            Assert.Equal("ArchiveTransitionResult", archive[id].Result!.ValueCkRecordId!.ElementId.Name);
        }

        Assert.Equal(AttributeValueTypesDto.Boolean, archive["DeleteArchive-1"].Result!.ValueType);
        Assert.True(archive["Activate-1"].Execution!.Idempotent, "activateArchive is idempotent on Activated");

        var creates = new[] { ("TimeRangeArchive", "TimeRangeArchiveCreateInput"), ("RollupArchive", "RollupArchiveCreateInput") };
        foreach (var (type, input) in creates)
        {
            var create = Assert.Single(types[type].Methods!, m => m.Kind == CkMethodKindDto.Static);
            Assert.Equal("CreateArchive-1", create.MethodId);
            Assert.Equal(CkMethodKindDto.Static, create.Kind);
            var parameter = Assert.Single(create.Parameters!);
            Assert.Equal(("archive", AttributeValueTypesDto.Record, input),
                (parameter.Name, parameter.ValueType, parameter.ValueCkRecordId!.ElementId.Name));
            Assert.Equal(AttributeValueTypesDto.String, create.Result!.ValueType);
        }

        Assert.Null(types["RawArchive"].Methods);
        Assert.Null(types["RecomputeJob"].Methods);

        var all = archive.Values.Concat(types["TimeRangeArchive"].Methods!).Concat(types["RollupArchive"].Methods!).ToList();
        Assert.Equal(12, all.Count);
        Assert.All(all, m =>
        {
            Assert.Equal(["StreamDataAdmin"], m.Authorization!.Roles!);
            Assert.Equal(ArchiveMethodErrorCodes, m.Errors!.Select(e => e.Code).Order(StringComparer.Ordinal));
            Assert.True(m.Execution!.TimeoutSeconds is > 0, m.MethodId);
            Assert.Null(m.Execution.Mode);
            Assert.False(m.ReplacesGenericCreate, m.MethodId);
            Assert.DoesNotContain(m.Parameters ?? [], p => p.Sensitive);
        });
    }

    /// <summary>
    ///     AB#8599: rollup control and recomputation as instance methods of RollupArchive, with the arguments and answers of
    ///     freezeRollupArchive, unfreezeRollupArchive, rewindRollupWatermark, recomputeArchive and backfillRollupFromSource.
    /// </summary>
    [Fact]
    public async Task RollupControlMethods_CarryTheContractOfTheMutations()
    {
        var compiled = await CompileStreamDataAsync(RealStreamDataSource());
        var rollup = compiled.Types!.Single(t => t.TypeId.Name == "RollupArchive").Methods!.ToDictionary(m => m.MethodId);

        Assert.Equal(["BackfillFromSource-1", "CreateArchive-1", "Freeze-1", "Recompute-1", "RewindWatermark-1", "Unfreeze-1"],
            rollup.Keys.Order(StringComparer.Ordinal));
        Assert.All(rollup.Values.Where(m => m.MethodId != "CreateArchive-1"),
            m => Assert.Equal(CkMethodKindDto.Instance, m.Kind));

        static (string, AttributeValueTypesDto, bool)[] Parameters(CkMethodDto m) =>
            (m.Parameters ?? []).Select(p => (p.Name, p.ValueType, p.IsOptional)).ToArray();

        Assert.Equal([("until", AttributeValueTypesDto.DateTime, false)], Parameters(rollup["Freeze-1"]));
        Assert.Equal([("acceptGaps", AttributeValueTypesDto.Boolean, true)], Parameters(rollup["Unfreeze-1"]));
        Assert.Equal([("toBucketEnd", AttributeValueTypesDto.DateTime, false)], Parameters(rollup["RewindWatermark-1"]));
        Assert.Equal(
            [("from", AttributeValueTypesDto.DateTime, false), ("to", AttributeValueTypesDto.DateTime, false),
             ("rtIdScope", AttributeValueTypesDto.String, true)],
            Parameters(rollup["Recompute-1"]));
        Assert.Empty(Parameters(rollup["BackfillFromSource-1"]));

        foreach (var id in new[] { "Freeze-1", "Unfreeze-1", "RewindWatermark-1" })
        {
            Assert.Equal("ArchiveTransitionResult", rollup[id].Result!.ValueCkRecordId!.ElementId.Name);
        }

        foreach (var id in new[] { "Recompute-1", "BackfillFromSource-1" })
        {
            Assert.Equal("RecomputeJobInfo", rollup[id].Result!.ValueCkRecordId!.ElementId.Name);
            Assert.False(rollup[id].Execution!.Idempotent, id);
            Assert.Null(rollup[id].Execution!.Mode); // synchronous, as the mutations; async is a later decision (F3.6)
        }

        var jobInfo = compiled.Records!.Single(r => r.RecordId.Name == "RecomputeJobInfo");
        Assert.Equal(["DurationMs", "ErrorReason", "FinishedAt", "JobRtId", "LastProgressAt", "RowsProcessed", "StartedAt", "State",
                "WindowsProcessed"],
            jobInfo.Attributes!.Select(a => a.AttributeName).Order(StringComparer.Ordinal));
    }

    /// <summary>
    ///     AB#8599: 1.25.0 against 1.24.0 (the same sources without the rollup control methods, RecomputeJobInfo and its
    ///     attributes) is a Minor without acknowledgement.
    /// </summary>
    [Fact]
    public async Task StreamData_1_25_0_AgainstThe_1_24_0_Release_IsAMinor_WithoutAcknowledgement()
    {
        var baseline = CopyStreamData("streamdata-1.24.0", (path, yaml) => path switch
        {
            "ckModel.yaml" => SetVersion(yaml, "1.24.0"),
            "types/rollupArchive.yaml" => CutFrom(yaml, "      # AB#8599 (1.25.0)"),
            "records/archiveMethodRecords.yaml" => CutFrom(yaml, "  # AB#8599 (System.StreamData 1.25.0)"),
            "attributes/archiveMethodAttributes.yaml" => CutFrom(yaml, "  # ---------- RecomputeJobInfo (AB#8599"),
            _ => yaml
        });
        var compiled = await CompileStreamDataAsync(baseline);
        Assert.Single(compiled.Types!.Single(t => t.TypeId.Name == "RollupArchive").Methods!);
        await _fixture.Services.GetRequiredService<ICatalogService>().PublishAsync(InMemoryPublishedCatalog.Name, compiled,
            new OriginFileResolver(baseline), isForced: true);

        var result = await RunGateAsync(RealStreamDataSource());

        Assert.Empty(Errors(result));
        Assert.Contains("1.24.0", result.Verdict!.BaselineId.ToString());
        Assert.Equal(CkSemVerLevel.Minor, result.Verdict.RequiredLevel);
        Assert.DoesNotContain(result.Verdict.ClassifiedChanges, c => c.RequiresAcknowledge);
    }

    /// <summary>The YAML up to (not including) the line that starts with <paramref name="marker" />.</summary>
    private static string CutFrom(string yaml, string marker)
    {
        var index = yaml.IndexOf("\n" + marker, StringComparison.Ordinal);
        Assert.True(index >= 0, $"marker '{marker}' not found");
        return yaml[..(index + 1)];
    }

    /// <summary>
    ///     AB#8598: 1.24.0 against 1.23.0 (ckLanguage 2, the same sources without the methods and their records) is a Minor
    ///     without acknowledgement: methods and records are additions only.
    /// </summary>
    [Fact]
    public async Task StreamData_1_24_0_AgainstTheMethodlessRelease_1_23_0_IsAMinor_WithoutAcknowledgement()
    {
        var baseline = CopyStreamData("streamdata-1.23.0", (path, yaml) => path == "ckModel.yaml"
            ? SetVersion(yaml, "1.23.0")
            : MethodOnlyFiles.Contains(path)
                ? RemoveElementsOf(yaml)
                : RemoveMethods(yaml));
        var compiled = await CompileStreamDataAsync(baseline);
        Assert.Equal(2, compiled.EffectiveCkLanguage);
        Assert.All(compiled.Types!, t => Assert.True(t.Methods is null || t.Methods.Count == 0, t.TypeId.Name));
        await _fixture.Services.GetRequiredService<ICatalogService>().PublishAsync(InMemoryPublishedCatalog.Name, compiled,
            new OriginFileResolver(baseline), isForced: true);

        var result = await RunGateAsync(RealStreamDataSource());

        Assert.Empty(Errors(result));
        Assert.Contains("1.23.0", result.Verdict!.BaselineId.ToString());
        Assert.Equal(CkSemVerLevel.Minor, result.Verdict.RequiredLevel);
        Assert.DoesNotContain(result.Verdict.ClassifiedChanges, c => c.Level == CkSemVerLevel.Major);
        Assert.DoesNotContain(result.Verdict.ClassifiedChanges, c => c.RequiresAcknowledge);
    }
}
