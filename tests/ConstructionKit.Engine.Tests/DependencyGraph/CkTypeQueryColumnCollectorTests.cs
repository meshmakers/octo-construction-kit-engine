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
}