using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;
using Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.systemFake;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.DependencyGraph;

public class CkTypeQueryColumnCollectorTests
{
    private static void FixModelGraph(CkModelGraph modelGraph)
    {
        foreach (var ckTypeGraph in modelGraph.Types.Values)
        {
            foreach (var ckTypeAttributeDto in ckTypeGraph.DefinedAttributes)
            {
                if (modelGraph.Attributes.TryGetValue(ckTypeAttributeDto.CkAttributeId, out var ckTypeAttributeGraph))
                {
                    ckTypeGraph.TryAddAttribute(new CkTypeAttributeGraph(ckTypeAttributeGraph.CkAttributeId,
                        ckTypeAttributeDto,
                        ckTypeAttributeGraph));
                }
            }
        }

        foreach (var ckRecordGraph in modelGraph.Records.Values)
        {
            foreach (var ckTypeAttributeDto in ckRecordGraph.DefinedAttributes)
            {
                if (modelGraph.Attributes.TryGetValue(ckTypeAttributeDto.CkAttributeId, out var ckTypeAttributeGraph))
                {
                    ckRecordGraph.TryAddAttribute(new CkTypeAttributeGraph(ckTypeAttributeGraph.CkAttributeId,
                        ckTypeAttributeDto,
                        ckTypeAttributeGraph));
                }
            }
        }
    }

    [Fact]
    public void Simple_OK()
    {
        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(Builder.Build());
        modelGraph.AppendModel(sampleData.sample1.Builder.Build());
        FixModelGraph(modelGraph);

        var ckTypeQueryColumnCollector = new CkTypeQueryColumnCollector(modelGraph);

        var result = ckTypeQueryColumnCollector.GetColumns("sample1/Demo1", new CkTypeQueryColumnOptions { IgnoreNavigationProperties = true });
        Assert.Equal<object>(
            ["a", "b", "c", "rtId", "ckTypeId", "rtWellKnownName", "rtVersion", "rtCreationDateTime", "rtChangedDateTime"],
            result.Select(x => x.Path));
        Assert.Equal<object>(["A", "B", "C", "RtId", "CkTypeId", "RtWellKnownName", "RtVersion", "RtCreationDateTime",
            "RtChangedDateTime"], result.SelectMany(x => x.AccessPathList.Select(y => y.Value)));
    }

    [Fact]
    public void Records_OK()
    {
        CkModelGraph modelGraph = new();
        modelGraph.AppendModel(Builder.Build());
        modelGraph.AppendModel(sampleData.records2.Builder.Build());
        FixModelGraph(modelGraph);

        var ckTypeQueryColumnCollector = new CkTypeQueryColumnCollector(modelGraph);

        var result = ckTypeQueryColumnCollector.GetColumns("TestRecords/Demo1");
        Assert.Equal<object>(
        [
            "myAttributeA", "myAttributeB", "myAttributeC", "myRecord.myAttributeA", "myRecord.myAttributeB",
            "myRecord.myAttributeC", "rtId", "ckTypeId", "rtWellKnownName", "rtVersion", "rtCreationDateTime", "rtChangedDateTime"
        ], result.Select(x => x.Path));
        Assert.Equal<object>(
        [
            "MyAttributeA", "MyAttributeB", "MyAttributeC", "MyRecord", "MyAttributeA", "MyRecord", "MyAttributeB",
            "MyRecord", "MyAttributeC", "RtId", "CkTypeId", "RtWellKnownName", "RtVersion", "RtCreationDateTime",
            "RtChangedDateTime"
        ], result.SelectMany(x => x.AccessPathList.Select(y => y.Value)));
    }

    private static CkModelGraph BuildResolvedModelGraph(params CkCompiledModelRoot[] models)
    {
        CkModelGraph modelGraph = new();
        foreach (var model in models)
        {
            modelGraph.AppendModel(model);
        }

        FixModelGraph(modelGraph);

        var resolver = new InheritanceResolver(NullLogger<InheritanceResolver>.Instance);
        var result = new OperationResult();
        resolver.Resolve(modelGraph, new OriginFileResolver("TEST"), result);
        return modelGraph;
    }

    [Fact]
    public void MultiPath_BothPathsToSameTargetAreGenerated()
    {
        var modelGraph = BuildResolvedModelGraph(Builder.Build(), sampleData.multipath.Builder.Build());
        var collector = new CkTypeQueryColumnCollector(modelGraph);

        var result = collector.GetColumns("MultiPath/Root");
        var paths = result.Select(x => x.Path).ToList();

        // Both paths through different intermediate types must reach Target's "name" attribute
        Assert.Contains("linkA.multiPathMiddle1->linkC.multiPathTarget->name", paths);
        Assert.Contains("linkB.multiPathMiddle2->linkC.multiPathTarget->name", paths);
    }

    [Fact]
    public void Description_PropagatedFromAttribute()
    {
        var modelGraph = BuildResolvedModelGraph(Builder.Build(), sampleData.withDescriptions.Builder.Build());
        var collector = new CkTypeQueryColumnCollector(modelGraph);

        var result = collector.GetColumns("Described/Device", new CkTypeQueryColumnOptions { IgnoreNavigationProperties = true });

        var meterReading = result.Single(x => x.Path == "meterReading");
        Assert.Equal("Current meter reading value in kWh", meterReading.Description);

        var serialNumber = result.Single(x => x.Path == "serialNumber");
        Assert.Equal("Unique serial number of the device", serialNumber.Description);
    }

    [Fact]
    public void Description_SystemAttributes_AreNull()
    {
        var modelGraph = BuildResolvedModelGraph(Builder.Build(), sampleData.withDescriptions.Builder.Build());
        var collector = new CkTypeQueryColumnCollector(modelGraph);

        var result = collector.GetColumns("Described/Device", new CkTypeQueryColumnOptions { IgnoreNavigationProperties = true });

        var systemColumns = new[] { "rtId", "ckTypeId", "rtWellKnownName", "rtVersion", "rtCreationDateTime", "rtChangedDateTime" };
        foreach (var sysCol in systemColumns)
        {
            var column = result.Single(x => x.Path == sysCol);
            Assert.Null(column.Description);
        }
    }

    [Fact]
    public void Description_AttributeWithoutDescription_IsNull()
    {
        var modelGraph = BuildResolvedModelGraph(Builder.Build(), sampleData.withDescriptions.Builder.Build());
        var collector = new CkTypeQueryColumnCollector(modelGraph);

        var result = collector.GetColumns("Described/Device", new CkTypeQueryColumnOptions { IgnoreNavigationProperties = true });

        var status = result.Single(x => x.Path == "status");
        Assert.Null(status.Description);
    }

    [Fact]
    public void Description_RecordSubAttributes_Propagated()
    {
        var modelGraph = BuildResolvedModelGraph(Builder.Build(), sampleData.withDescriptions.Builder.Build());
        var collector = new CkTypeQueryColumnCollector(modelGraph);

        var result = collector.GetColumns("Described/Device", new CkTypeQueryColumnOptions { IgnoreNavigationProperties = true });

        // Record sub-attributes should propagate description from the referenced attribute
        var city = result.Single(x => x.Path == "location.city");
        Assert.Equal("Unique serial number of the device", city.Description);

        var country = result.Single(x => x.Path == "location.country");
        Assert.Null(country.Description);
    }

    [Fact]
    public void MaxDepth_Zero_NoNavigationColumns()
    {
        var modelGraph = BuildResolvedModelGraph(Builder.Build(), sampleData.withDescriptions.Builder.Build());
        var collector = new CkTypeQueryColumnCollector(modelGraph);

        var resultWithNav = collector.GetColumns("Described/Device");
        var resultWithMaxDepthZero = collector.GetColumns("Described/Device", new CkTypeQueryColumnOptions { MaxDepth = 0 });
        var resultIgnoreNav = collector.GetColumns("Described/Device", new CkTypeQueryColumnOptions { IgnoreNavigationProperties = true });

        // MaxDepth=0 should yield same paths as IgnoreNavigationProperties=true
        Assert.Equal(resultIgnoreNav.Select(x => x.Path).OrderBy(x => x),
            resultWithMaxDepthZero.Select(x => x.Path).OrderBy(x => x));

        // But less or equal to full navigation
        Assert.True(resultWithMaxDepthZero.Count <= resultWithNav.Count);
    }

    [Fact]
    public void MaxDepth_One_OnlyFirstLevelNavigations()
    {
        var modelGraph = BuildResolvedModelGraph(Builder.Build(), sampleData.withDescriptions.Builder.Build());
        var collector = new CkTypeQueryColumnCollector(modelGraph);

        var resultDepth1 = collector.GetColumns("Described/Device", new CkTypeQueryColumnOptions { MaxDepth = 1 });
        var paths = resultDepth1.Select(x => x.Path).ToList();

        // First-level navigation columns should be present (parent.xxx->yyy)
        Assert.True(paths.Any(p => p.Contains("->")), "Expected at least one navigation column at depth 1");

        // But no second-level navigation (two -> separators)
        Assert.DoesNotContain(paths, p => p.Split("->").Length > 2);
    }

    [Fact]
    public void MaxDepth_Null_UnlimitedDepth()
    {
        var modelGraph = BuildResolvedModelGraph(Builder.Build(), sampleData.withDescriptions.Builder.Build());
        var collector = new CkTypeQueryColumnCollector(modelGraph);

        var resultDefault = collector.GetColumns("Described/Device");
        var resultNullDepth = collector.GetColumns("Described/Device", new CkTypeQueryColumnOptions { MaxDepth = null });

        Assert.Equal(resultDefault.Select(x => x.Path).OrderBy(x => x),
            resultNullDepth.Select(x => x.Path).OrderBy(x => x));
    }

    [Fact]
    public void DefaultOptions_SameAsNoOptions()
    {
        var modelGraph = BuildResolvedModelGraph(Builder.Build(), sampleData.withDescriptions.Builder.Build());
        var collector = new CkTypeQueryColumnCollector(modelGraph);

        var resultNull = collector.GetColumns("Described/Device");
        var resultDefault = collector.GetColumns("Described/Device", CkTypeQueryColumnOptions.Default);

        Assert.Equal(resultNull.Select(x => x.Path).OrderBy(x => x),
            resultDefault.Select(x => x.Path).OrderBy(x => x));
    }

    [Fact]
    public void NtoM_Outbound_GeneratesTotalCountAndExists()
    {
        var modelGraph = BuildResolvedModelGraph(Builder.Build(), sampleData.nToMAssociations.Builder.Build());
        var collector = new CkTypeQueryColumnCollector(modelGraph);

        var result = collector.GetColumns("NToMTest/Document");
        var paths = result.Select(x => x.Path).ToList();

        // Outbound N:M association TransactionToDocument should generate totalCount and exists columns
        Assert.Contains(paths, p => p.Contains("transactions") && p.EndsWith("::totalCount"));
        Assert.Contains(paths, p => p.Contains("transactions") && p.EndsWith("::exists"));

        // Verify value types
        var totalCountCol = result.Single(x => x.Path.EndsWith("::totalCount") && x.Path.Contains("transactions"));
        Assert.Equal(AttributeValueTypesDto.Int64, totalCountCol.ValueType);
        Assert.NotNull(totalCountCol.AssociationTuple);
        Assert.Equal(MultiplicitiesDto.N, totalCountCol.AssociationTuple.Multiplicity);

        var existsCol = result.Single(x => x.Path.EndsWith("::exists") && x.Path.Contains("transactions"));
        Assert.Equal(AttributeValueTypesDto.Boolean, existsCol.ValueType);
        Assert.NotNull(existsCol.AssociationTuple);
    }

    [Fact]
    public void NtoM_Inbound_GeneratesTotalCountAndExists()
    {
        var modelGraph = BuildResolvedModelGraph(Builder.Build(), sampleData.nToMAssociations.Builder.Build());
        var collector = new CkTypeQueryColumnCollector(modelGraph);

        // Transaction has an inbound N:M from Document (Documents)
        var result = collector.GetColumns("NToMTest/Transaction");
        var paths = result.Select(x => x.Path).ToList();

        Assert.Contains(paths, p => p.Contains("documents") && p.EndsWith("::totalCount"));
        Assert.Contains(paths, p => p.Contains("documents") && p.EndsWith("::exists"));
    }

    [Fact]
    public void NtoM_NoDuplicates_SingleColumnPerNavigationProperty()
    {
        var modelGraph = BuildResolvedModelGraph(Builder.Build(), sampleData.nToMAssociations.Builder.Build());
        var collector = new CkTypeQueryColumnCollector(modelGraph);

        var result = collector.GetColumns("NToMTest/Document");

        // Should have exactly one totalCount and one exists per N:M navigation property
        var totalCountColumns = result.Where(x => x.Path.EndsWith("::totalCount")).ToList();
        var existsColumns = result.Where(x => x.Path.EndsWith("::exists")).ToList();

        // Document has 2 N:M navs: transactions (outbound) + relatesTo (from System/Entity)
        // Each should appear exactly once
        Assert.Equal(totalCountColumns.Select(x => x.Path).Distinct().Count(), totalCountColumns.Count);
        Assert.Equal(existsColumns.Select(x => x.Path).Distinct().Count(), existsColumns.Count);
    }

    [Fact]
    public void NtoM_OneToN_OnlyOutboundGeneratesColumns()
    {
        var modelGraph = BuildResolvedModelGraph(Builder.Build(), sampleData.nToMAssociations.Builder.Build());
        var collector = new CkTypeQueryColumnCollector(modelGraph);

        // Category has outbound 1:N CategoryLink to Document (outbound multiplicity = N)
        var result = collector.GetColumns("NToMTest/Category");
        var paths = result.Select(x => x.Path).ToList();

        // 1:N outbound should generate totalCount/exists for the N side
        Assert.Contains(paths, p => p.Contains("items") && p.EndsWith("::totalCount"));
        Assert.Contains(paths, p => p.Contains("items") && p.EndsWith("::exists"));
    }

    [Fact]
    public void NtoM_IgnoreNavigationProperties_NoNtoMColumns()
    {
        var modelGraph = BuildResolvedModelGraph(Builder.Build(), sampleData.nToMAssociations.Builder.Build());
        var collector = new CkTypeQueryColumnCollector(modelGraph);

        var result = collector.GetColumns("NToMTest/Document",
            new CkTypeQueryColumnOptions { IgnoreNavigationProperties = true });
        var paths = result.Select(x => x.Path).ToList();

        Assert.DoesNotContain(paths, p => p.Contains("::totalCount"));
        Assert.DoesNotContain(paths, p => p.Contains("::exists"));
    }

    [Fact]
    public void NtoM_PathFormat_UsesDoubleColonSeparator()
    {
        var modelGraph = BuildResolvedModelGraph(Builder.Build(), sampleData.nToMAssociations.Builder.Build());
        var collector = new CkTypeQueryColumnCollector(modelGraph);

        var result = collector.GetColumns("NToMTest/Document");
        var nToMColumns = result.Where(x =>
            x.AssociationTuple is { Multiplicity: MultiplicitiesDto.N }).ToList();

        // All N:M columns must use :: separator (not ->)
        foreach (var col in nToMColumns)
        {
            Assert.Contains("::", col.Path);
            Assert.DoesNotContain("->", col.Path);
        }
    }

    [Fact]
    public void NtoM_AccessPathList_ContainsNavigationAndTargetCkTypeId()
    {
        var modelGraph = BuildResolvedModelGraph(Builder.Build(), sampleData.nToMAssociations.Builder.Build());
        var collector = new CkTypeQueryColumnCollector(modelGraph);

        var result = collector.GetColumns("NToMTest/Document");
        var totalCountCol = result.Single(x => x.Path.EndsWith("::totalCount") && x.Path.Contains("transactions"));

        var accessPath = totalCountCol.AccessPathList.ToList();
        Assert.Equal(2, accessPath.Count);
        Assert.Equal(PathType.Navigation, accessPath[0].Type);
        Assert.Equal("Transactions", accessPath[0].Value);
        Assert.Equal(PathType.TargetCkTypeId, accessPath[1].Type);
    }

    [Fact]
    public void IsDataStream_SystemFields_CorrectValues()
    {
        var modelGraph = BuildResolvedModelGraph(Builder.Build(), sampleData.sample1.Builder.Build());
        var collector = new CkTypeQueryColumnCollector(modelGraph);

        var result = collector.GetColumns("sample1/Demo1", new CkTypeQueryColumnOptions { IgnoreNavigationProperties = true });

        Assert.True(result.Single(x => x.Path == "rtId").IsDataStream);
        Assert.True(result.Single(x => x.Path == "ckTypeId").IsDataStream);
        Assert.True(result.Single(x => x.Path == "rtWellKnownName").IsDataStream);
        Assert.False(result.Single(x => x.Path == "rtVersion").IsDataStream);
        Assert.True(result.Single(x => x.Path == "rtCreationDateTime").IsDataStream);
        Assert.True(result.Single(x => x.Path == "rtChangedDateTime").IsDataStream);
    }

    [Fact]
    public void IsDataStream_CkAttributeWithDataStream_IsTrue()
    {
        var modelGraph = BuildResolvedModelGraph(Builder.Build(), sampleData.withDataStream.Builder.Build());
        var collector = new CkTypeQueryColumnCollector(modelGraph);

        var result = collector.GetColumns("StreamTest/Sensor", new CkTypeQueryColumnOptions { IgnoreNavigationProperties = true });

        Assert.True(result.Single(x => x.Path == "temperature").IsDataStream);
        Assert.True(result.Single(x => x.Path == "pressure").IsDataStream);
    }

    [Fact]
    public void IsDataStream_CkAttributeWithoutDataStream_IsFalse()
    {
        var modelGraph = BuildResolvedModelGraph(Builder.Build(), sampleData.withDataStream.Builder.Build());
        var collector = new CkTypeQueryColumnCollector(modelGraph);

        var result = collector.GetColumns("StreamTest/Sensor", new CkTypeQueryColumnOptions { IgnoreNavigationProperties = true });

        Assert.False(result.Single(x => x.Path == "deviceName").IsDataStream);
        Assert.False(result.Single(x => x.Path == "location").IsDataStream);
    }
}