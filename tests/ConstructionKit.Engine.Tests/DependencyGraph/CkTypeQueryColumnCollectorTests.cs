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

        var result = ckTypeQueryColumnCollector.GetColumns("sample1/Demo1", true);
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
}