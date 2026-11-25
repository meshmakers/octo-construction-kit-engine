using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     AB#6929 (Epic AB#5584 F4.1): the real System sources declare the interfaces <c>Named-1</c>, <c>Describable-1</c>,
///     <c>Enableable-1</c> and <c>Hierarchical-1</c>. The tests pin the contract (members, extends, role), the per-type
///     implementation table, the SemVer verdict against System 2.5.0 and 2.6.0 (Minor), and that a model of another
///     repository can implement them. Nothing is published outside the in-memory and the test-private catalogs.
/// </summary>
public sealed class SystemInterfacesTests : IDisposable
{
    private readonly InMemoryPublishedCatalog _published = new();
    private readonly CkCompileFixture _fixture;

    public SystemInterfacesTests()
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

    private string CopySystem(string name, Func<string, string, string> transform)
    {
        var source = RealSystemSource();
        var target = Path.Combine(_fixture.Root, "src", name, "ConstructionKit");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, relative.EndsWith(".yaml", StringComparison.Ordinal)
                ? transform(relative.Replace('\\', '/'), File.ReadAllText(file))
                : File.ReadAllText(file));
        }

        return target;
    }

    private static string SetVersion(string yaml, string version) =>
        System.Text.RegularExpressions.Regex.Replace(yaml, @"modelId: ""System-[\d.]+""", $"modelId: \"System-{version}\"");

    private static string RemoveLine(string yaml, string line) =>
        string.Join("\n", yaml.Split('\n').Where(l => l.Trim() != line));

    /// <summary>System 2.6.0 as it was published: ckLanguage 2, no interface and no <c>implements</c>.</summary>
    private string CopySystem260()
    {
        var dir = CopySystem("system-2.6.0", (path, yaml) => path switch
        {
            "ckModel.yaml" => SetVersion(yaml, "2.6.0"),
            "types/query.yaml" => RemoveLine(RemoveLine(RemoveLine(yaml, "implements:"), "- ${this}/Named-1"),
                "- ${this}/Describable-1"),
            _ => yaml
        });
        Directory.Delete(Path.Combine(dir, "interfaces"), true);
        return dir;
    }

    /// <summary>System 2.5.0: the ckLanguage 1 System, same sources without language key, modifiers and interfaces.</summary>
    private string CopySystem250()
    {
        var dir = CopySystem("system-2.5.0", (path, yaml) => path switch
        {
            "ckModel.yaml" => RemoveLine(SetVersion(yaml, "2.5.0"), "ckLanguage: 2"),
            "types/query.yaml" => RemoveLine(RemoveLine(RemoveLine(RemoveLine(yaml, "implements:"), "- ${this}/Named-1"),
                "- ${this}/Describable-1"), "derivable: Any"),
            _ => RemoveLine(yaml, "derivable: Any")
        });
        Directory.Delete(Path.Combine(dir, "interfaces"), true);
        return dir;
    }

    /// <summary>The files System 2.9.0 adds (AB#9752): number ranges, release configuration and the document audit trail.</summary>
    private static readonly string[] System29Files =
    [
        "attributes/numberRange.yaml", "attributes/documentAudit.yaml", "records/numberRange.yaml",
        "enums/numberRange.yaml", "types/documentAudit.yaml"
    ];

    /// <summary>
    ///     Cuts the 2.9.0 additions off an existing file: the new <c>AutoIncrement</c> attributes and the
    ///     <c>Cancels</c> association role are appended after a comment line that starts with this marker.
    /// </summary>
    private static string WithoutSystem29(string path, string yaml)
    {
        if (path is not ("types/autoIncrement.yaml" or "associations/ck-associations.yaml"))
        {
            return yaml;
        }

        var marker = yaml.IndexOf("# System 2.9.0 (AB#9752)", StringComparison.Ordinal);
        Assert.True(marker > 0, $"2.9.0 marker missing in {path}");
        return yaml[..(yaml.LastIndexOf('\n', marker) + 1)];
    }

    private const string RtSyncOriginOnEntity =
        "      - id: ${this}/RtSyncOrigin\n        name: RtSyncOrigin\n        isOptional: true\n";

    /// <summary>The files of the <c>System/SyncOrigin</c> record (AB#9770).</summary>
    private static readonly string[] SyncOriginFiles = ["records/syncOrigin.yaml", "attributes/syncOrigin.yaml"];

    /// <summary>
    ///     Removes the <c>System/SyncOrigin</c> record (AB#9770) from a copy of the sources: the optional
    ///     <c>RtSyncOrigin</c> attribute of <c>System/Entity</c> here, its two files in <see cref="SyncOriginFiles" />.
    /// </summary>
    private static string WithoutSyncOrigin(string path, string yaml)
    {
        if (path != "types/entity.yaml")
        {
            return yaml;
        }

        Assert.Contains(RtSyncOriginOnEntity, yaml, StringComparison.Ordinal);
        return yaml.Replace(RtSyncOriginOnEntity, "", StringComparison.Ordinal);
    }

    private static void DeleteFiles(string dir, IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            File.Delete(Path.Combine(dir, file));
        }
    }

    /// <summary>
    ///     The real sources without the 2.9.0 additions (and the later <c>SyncOrigin</c> record), at
    ///     <paramref name="version"/>, then <paramref name="transform"/>.
    /// </summary>
    private string CopySystemBefore29(string name, string version, Func<string, string, string> transform)
    {
        var dir = CopySystem(name, (path, yaml) =>
            transform(path, path == "ckModel.yaml" ? SetVersion(yaml, version) : WithoutSyncOrigin(path, WithoutSystem29(path, yaml))));
        DeleteFiles(dir, System29Files);
        DeleteFiles(dir, SyncOriginFiles);
        return dir;
    }

    /// <summary>System 2.8.0 as it was published: the 2.9.0 sources without the number-range and document-audit additions (AB#9752).</summary>
    private string CopySystem280() => CopySystemBefore29("system-2.8.0", "2.8.0", (_, yaml) => yaml);

    /// <summary>System 2.7.0 as it was published: the 2.8.0 sources without the <c>Integral</c> aggregation type (AB#7918).</summary>
    private string CopySystem270() =>
        CopySystemBefore29("system-2.7.0", "2.7.0", (path, yaml) => path == "enums/aggregationTypes.yaml"
            ? yaml[..yaml.IndexOf("  - key: 7", StringComparison.Ordinal)]
            : yaml);

    private async Task PublishAsync(string sourceDir, string catalog)
    {
        var compiled = await _fixture.CompileAsync(sourceDir);
        await _fixture.Services.GetRequiredService<ICatalogService>().PublishAsync(catalog, compiled,
            new OriginFileResolver(sourceDir), isForced: true);
    }

    private async Task<CkCompileGateResult> RunGateAsync(string sourceDir)
    {
        var operationResult = new OperationResult();
        var output = Path.Combine(_fixture.Root, "out", Guid.NewGuid().ToString("N"));
        var compileResult = await _fixture.Services.GetRequiredService<ICompilerService>()
            .CompileAsync(sourceDir, output, null, operationResult);
        Assert.False(operationResult.HasErrors, string.Join(Environment.NewLine, operationResult.Messages));
        await using var stream = File.OpenRead(compileResult.CompiledModelFile);
        var root = await _fixture.Services.GetRequiredService<Meshmakers.Octo.ConstructionKit.Contracts.Serialization.ICkSerializer>()
            .DeserializeCompiledModelRootAsync(stream, compileResult.CompiledModelFile, operationResult);
        return await _fixture.Services.GetRequiredService<CkCompileGate>().RunAsync(root, CkBaselineSource.Remote);
    }

    private static string[] Errors(CkCompileGateResult result) =>
        result.Messages.Where(m => m.Severity == CkCompileGateSeverity.Error).Select(m => m.Code ?? "").ToArray();

    [Fact]
    public async Task System_DeclaresTheFourInterfaces_WithTheirMembers()
    {
        var compiled = await _fixture.CompileAsync(RealSystemSource());
        var interfaces = compiled.Interfaces!.ToDictionary(i => i.InterfaceId.ToString()!);

        Assert.Equal(["Describable-1", "Enableable-1", "Hierarchical-1", "Named-1"], interfaces.Keys.Order());

        var named = interfaces["Named-1"];
        Assert.Equal(["Name"], named.Attributes.Select(a => a.AttributeName));
        Assert.All(named.Attributes, a => Assert.False(a.IsOptional));
        Assert.Null(named.Associations);

        var describable = interfaces["Describable-1"];
        Assert.Equal(["Description"], describable.Attributes.Select(a => a.AttributeName));
        Assert.True(describable.Attributes.Single().IsOptional);

        var enableable = interfaces["Enableable-1"];
        Assert.Equal(["Enabled"], enableable.Attributes.Select(a => a.AttributeName));
        Assert.False(enableable.Attributes.Single().IsOptional);

        var hierarchical = interfaces["Hierarchical-1"];
        Assert.Empty(hierarchical.Attributes);
        Assert.Equal(["Named-1"], hierarchical.Extends!.Select(e => e.ToString()!.Split('/')[1]));
        var parent = Assert.Single(hierarchical.Associations!);
        Assert.EndsWith("/ParentChild-1", parent.CkRoleId.ToString());
        Assert.EndsWith("/Hierarchical-1", parent.TargetCkInterfaceId!.ToString());
        Assert.Null(parent.TargetCkTypeId);
        Assert.False(parent.IsOptional);

        // The interfaces add no association role; Cancels comes with 2.9.0 (AB#9752, storno of a released document).
        Assert.Equal(["Cancels-1", "ParentChild-1", "Related-1"], compiled.AssociationRoles!.Select(r => r.AssociationRoleId.ToString()!.Split('/').Last()).Order());
    }

    [Fact]
    public async Task OnlyThePersistentQueryHierarchy_ImplementsSystemInterfaces_AndNoIndexOnNameIsAdded()
    {
        var baseline = await _fixture.CompileAsync(CopySystem250());
        var compiled = await _fixture.CompileAsync(RealSystemSource());

        var implementers = compiled.Types!.Where(t => t.Implements is { Count: > 0 }).ToList();
        var query = Assert.Single(implementers);
        Assert.Equal("PersistentQuery", query.TypeId.Name);
        Assert.Equal(["Describable-1", "Named-1"], query.Implements!.Select(i => i.ToString()!.Split('/')[1]).Order());

        // Decision of 2026-10-19 (AB#7019): Named-1 comes with no index; the indexes of every type are unchanged.
        foreach (var type in compiled.Types!)
        {
            var before = baseline.Types!.Single(t => t.TypeId.Name == type.TypeId.Name);
            Assert.Equal(before.Indexes?.Count ?? 0, type.Indexes?.Count ?? 0);
        }
    }

    [Fact]
    public async Task System27_AgainstSystem250_AndAgainstSystem260_IsAMinor()
    {
        await PublishAsync(CopySystem250(), InMemoryPublishedCatalog.Name);
        var against250 = await RunGateAsync(RealSystemSource());

        Assert.Empty(Errors(against250));
        Assert.Equal(CkSemVerLevel.Minor, against250.Verdict!.RequiredLevel);
        Assert.Contains("2.5.0", against250.Verdict.BaselineId.ToString());
        Assert.DoesNotContain(against250.Verdict.ClassifiedChanges, c => c.Level == CkSemVerLevel.Major);

        await PublishAsync(CopySystem260(), InMemoryPublishedCatalog.Name);
        var against260 = await RunGateAsync(RealSystemSource());

        Assert.Empty(Errors(against260));
        Assert.Equal(CkSemVerLevel.Minor, against260.Verdict!.RequiredLevel);
        Assert.Contains("2.6.0", against260.Verdict.BaselineId.ToString());
        Assert.DoesNotContain(against260.Verdict.ClassifiedChanges, c => c.Level == CkSemVerLevel.Major);
        // Four new interfaces and two new implements entries (PersistentQuery), all additive.
        Assert.Equal(4, against260.Verdict.ClassifiedChanges.Count(c =>
            c.Change.ElementKind == CkModelElementKind.Interface && c.Change.ChangeKind == CkModelChangeKind.Added));
    }

    [Fact]
    public async Task System28_AgainstSystem270_IsAMinor_WithTheIntegralAggregationTypeAsTheOnlyChange()
    {
        // AB#7918: AggregationTypes gains Integral (key 7). An added enum value is additive (Minor),
        // needs no migration, and stored AggregationSdQuery entities keep their values.
        await PublishAsync(CopySystem270(), InMemoryPublishedCatalog.Name);
        var against270 = await RunGateAsync(CopySystem280());

        Assert.Empty(Errors(against270));
        Assert.Equal(CkSemVerLevel.Minor, against270.Verdict!.RequiredLevel);
        Assert.Contains("2.7.0", against270.Verdict.BaselineId.ToString());
        var structural = against270.Verdict.ClassifiedChanges
            .Where(c => c.Change.ElementKind != CkModelElementKind.Model)
            .ToList();
        var change = Assert.Single(structural);
        Assert.Equal(CkModelElementKind.EnumValue, change.Change.ElementKind);
        Assert.Equal(CkModelChangeKind.Added, change.Change.ChangeKind);
        Assert.Contains("AggregationTypes", change.Change.ElementId, StringComparison.Ordinal);
        Assert.Equal(CkSemVerLevel.Minor, change.Level);
    }

    [Fact]
    public async Task System29_AgainstSystem280_IsAMinor_WithOnlyAddedElements()
    {
        // AB#9752: number ranges (AB#8675) and released-document locks (AB#8676) ship in one System bump.
        // Every change is an addition (optional attributes on AutoIncrement, new records, enums, a type and an
        // association role), so 2.8.0 -> 2.9.0 is a Minor, needs no migration and dependents need no re-pin.
        await PublishAsync(CopySystem280(), InMemoryPublishedCatalog.Name);
        var against280 = await RunGateAsync(RealSystemSource());

        Assert.Empty(Errors(against280));
        Assert.Equal(CkSemVerLevel.Minor, against280.Verdict!.RequiredLevel);
        Assert.Contains("2.8.0", against280.Verdict.BaselineId.ToString());
        var structural = against280.Verdict.ClassifiedChanges
            .Where(c => c.Change.ElementKind != CkModelElementKind.Model)
            .ToList();
        Assert.NotEmpty(structural);
        Assert.All(structural, c =>
        {
            Assert.Equal(CkModelChangeKind.Added, c.Change.ChangeKind);
            Assert.NotEqual(CkSemVerLevel.Major, c.Level);
        });
        // AB#9770: System/SyncOrigin and the optional System/Entity attribute RtSyncOrigin are additions too.
        Assert.Contains(structural, c => c.Change.ElementKind == CkModelElementKind.Record &&
                                         c.Change.ElementId.Contains("SyncOrigin", StringComparison.Ordinal));
        Assert.Contains(structural, c => c.Change.ElementKind == CkModelElementKind.TypeAttribute &&
                                         c.Change.ElementId.Contains("Entity", StringComparison.Ordinal) &&
                                         c.Change.ElementId.Contains("RtSyncOrigin", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SyncOrigin_IsAnOptionalRecordAttributeOfEntity_WithSourceTenantSourceRtIdAndSyncTime()
    {
        var compiled = await _fixture.CompileAsync(RealSystemSource());

        var record = Assert.Single(compiled.Records!, r => r.RecordId.Name == "SyncOrigin");
        Assert.Equal(CkDerivableDto.Any, record.Derivable);
        Assert.Equal(["SourceRtId", "SourceTenantId", "SyncedAt"],
            record.Attributes!.Select(a => a.AttributeName).Order(StringComparer.Ordinal));

        var attributes = compiled.Attributes!.ToDictionary(a => a.AttributeId.Name, StringComparer.Ordinal);
        Assert.Equal(AttributeValueTypesDto.String, attributes["SyncOrigin.SourceTenantId"].ValueType);
        Assert.Equal(AttributeValueTypesDto.String, attributes["SyncOrigin.SourceRtId"].ValueType);
        Assert.Equal(AttributeValueTypesDto.DateTime, attributes["SyncOrigin.SyncedAt"].ValueType);
        Assert.Equal(AttributeValueTypesDto.Record, attributes["RtSyncOrigin"].ValueType);
        Assert.Equal("SyncOrigin", attributes["RtSyncOrigin"].ValueCkRecordId!.ElementId.Name);

        var entity = Assert.Single(compiled.Types!, t => t.TypeId.Name == "Entity");
        var onEntity = Assert.Single(entity.Attributes!, a => a.AttributeName == "RtSyncOrigin");
        Assert.True(onEntity.IsOptional);
        // Nothing else of Entity changed: the three blueprint attributes stay optional, no attribute became required.
        Assert.All(entity.Attributes!, a => Assert.True(a.IsOptional, a.AttributeName));
    }

    [Fact]
    public async Task System29_NumberRangeAndAuditAdditions_AreOptional_SoExistingCountersStayValid()
    {
        // AB#9752: an existing counter (End, Format, CurrentValue only - the shape of the Basic and
        // energy-community seeds) must stay valid, so every new AutoIncrement attribute is optional.
        var compiled = await _fixture.CompileAsync(RealSystemSource());
        Assert.Contains("2.9.0", compiled.ModelId.ToString());

        var autoIncrement = Assert.Single(compiled.Types!, t => t.TypeId.Name == "AutoIncrement");
        var attributes = autoIncrement.Attributes!.ToDictionary(a => a.AttributeName);
        Assert.False(attributes["End"].IsOptional);
        Assert.False(attributes["CurrentValue"].IsOptional);
        foreach (var name in new[] { "Format", "StartValue", "PeriodKind", "FiscalYearStartMonth", "ReferenceTimeZone", "Periods", "Release" })
        {
            Assert.True(attributes[name].IsOptional, $"AutoIncrement.{name} must be optional");
        }

        Assert.Contains(compiled.Types!, t => t.TypeId.Name == "DocumentAuditEntry");
        Assert.Contains(compiled.Records!, r => r.RecordId.Name == "AutoIncrementPeriod");
        Assert.Contains(compiled.Records!, r => r.RecordId.Name == "NumberRangeRelease");
        Assert.Contains(compiled.Enums!, e => e.EnumId.Name == "AutoIncrementPeriodKinds");
        Assert.Contains(compiled.Enums!, e => e.EnumId.Name == "DocumentAuditActions");
        Assert.Contains(compiled.AssociationRoles!, r => r.AssociationRoleId.FullName.StartsWith("Cancels", StringComparison.Ordinal));
    }

    private async Task<(OperationResult Result, CkCompiledModelRoot? Compiled)> CompileProbeAsync(string typeYaml)
    {
        await PublishAsync(RealSystemSource(), LocalFileSystemCatalog.Name);
        var dependent = _fixture.WriteSource("probe-" + Guid.NewGuid().ToString("N"), "Probe-1.0.0", ["System-[2.7,3.0)"],
            new Dictionary<string, string> { ["types/t.yaml"] = typeYaml }, 2);
        var result = new OperationResult();
        CkCompiledModelRoot? compiled = null;
        try
        {
            compiled = await _fixture.Services.GetRequiredService<ICompilerService>().CompileInMemoryAsync(dependent, result);
        }
        catch (Exception)
        {
            // asserted through the messages
        }

        return (result, compiled);
    }

    [Fact]
    public async Task AModelOfAnotherRepository_ImplementsAllFourInterfaces()
    {
        var (result, compiled) = await CompileProbeAsync(
            "types:\n" +
            "  - typeId: Node\n" +
            "    derivedFromCkTypeId: ${System}/Entity\n" +
            "    derivable: Any\n" +
            "    implements:\n" +
            "      - ${System}/Hierarchical-1\n" +
            "      - ${System}/Describable-1\n" +
            "      - ${System}/Enableable-1\n" +
            "    attributes:\n" +
            "      - id: ${System}/Name\n        name: Name\n" +
            "      - id: ${System}/Description\n        name: Description\n        isOptional: true\n" +
            "      - id: ${System}/Enabled\n        name: Enabled\n" +
            "    associations:\n" +
            "      - id: ${System}/ParentChild\n        targetCkTypeId: ${this}/Node\n");

        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Messages));
        var node = compiled!.Types!.Single(t => t.TypeId.Name == "Node");
        Assert.Equal(["Describable-1", "Enableable-1", "Hierarchical-1"],
            node.Implements!.Select(i => i.ToString()!.Split('/')[1]).Order());
    }

    [Fact]
    public async Task ANamedType_ThatOnlyImplementsNamed1_NeedsNoIndexAndNoOtherMember()
    {
        var (result, compiled) = await CompileProbeAsync(
            "types:\n" +
            "  - typeId: Thing\n" +
            "    derivedFromCkTypeId: ${System}/Entity\n" +
            "    implements:\n      - ${System}/Named-1\n" +
            "    attributes:\n      - id: ${System}/Name\n        name: Name\n");

        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Messages));
        Assert.Null(compiled!.Types!.Single(t => t.TypeId.Name == "Thing").Indexes);
    }

    [Fact]
    public async Task AHierarchicalType_WithoutTheParentChildAssociation_IsRejectedWithMessage121()
    {
        var (result, _) = await CompileProbeAsync(
            "types:\n" +
            "  - typeId: Node\n" +
            "    derivedFromCkTypeId: ${System}/Entity\n" +
            "    implements:\n      - ${System}/Hierarchical-1\n" +
            "    attributes:\n      - id: ${System}/Name\n        name: Name\n");

        Assert.Contains(result.Messages, m => m.MessageNumber == 121 && m.MessageText.Contains("ParentChild"));
    }

    [Fact]
    public async Task AHierarchicalType_WithoutName_IsRejected_BecauseHierarchicalExtendsNamed()
    {
        var (result, _) = await CompileProbeAsync(
            "types:\n" +
            "  - typeId: Node\n" +
            "    derivedFromCkTypeId: ${System}/Entity\n" +
            "    implements:\n      - ${System}/Hierarchical-1\n" +
            "    associations:\n      - id: ${System}/ParentChild\n        targetCkTypeId: ${this}/Node\n");

        Assert.True(result.HasErrors);
        Assert.Contains(result.Messages, m => m.MessageText.Contains("Name"));
    }
}
