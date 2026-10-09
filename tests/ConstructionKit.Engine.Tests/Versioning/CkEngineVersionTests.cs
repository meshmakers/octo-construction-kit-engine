using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.Tests.Resolvers;
using Meshmakers.Octo.ConstructionKit.Engine.Versioning;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Versioning;

/// <summary>
///     F1.1-S6 (AB#5909): <c>minEngineVersion</c> of compiled models and message 126.
/// </summary>
public class CkEngineVersionTests(ITestOutputHelper output) : CkV2ResolverTestBase(output)
{
    [Theory]
    [InlineData(null, "3.4.0", true)]
    [InlineData("3.4.0", "3.4.0", true)]
    [InlineData("3.4.0", "3.4.149", true)]
    [InlineData("3.4.0", "999.0.0", true)]
    [InlineData("3.5.0", "3.4.149", false)]
    [InlineData("3.4.150", "3.4.149", false)]
    [InlineData("not-a-version", "3.4.0", false)]
    public void IsSatisfiedBy(string? minEngineVersion, string engineVersion, bool expected)
    {
        Assert.Equal(expected, CkEngineVersion.IsSatisfiedBy(minEngineVersion, Version.Parse(engineVersion)));
    }

    [Fact]
    public void UnknownEngineVersion_SkipsTheCheck()
    {
        Assert.True(CkEngineVersion.IsSatisfiedBy("1000.0.0", null));
    }

    [Fact]
    public void RequiredMinEngineVersion_OnlyForCkV2AndRangeRetainingOutput()
    {
        var v1 = sampleData.sample1.Builder.Build();
        Assert.Null(CkEngineVersion.GetRequiredMinEngineVersion(v1));

        Assert.Equal(CkEngineVersion.CkV2MinEngineVersion, CkEngineVersion.GetRequiredMinEngineVersion(Model()));

        var rangeRetaining = sampleData.sample1.Builder.Build();
        rangeRetaining.DependencyRanges = [];
        Assert.Equal(CkEngineVersion.CkV2MinEngineVersion, CkEngineVersion.GetRequiredMinEngineVersion(rangeRetaining));
    }

    [Fact]
    public void Code126_ModelRequiresANewerEngine()
    {
        // Pin the running engine: DebugL is 999.0.0, CI 0.1.* (check skipped), release builds 3.x (AB#6274).
        using var _ = CkEngineVersion.OverrideCurrentForTests(new Version(3, 4, 149));
        var model = Model();
        model.MinEngineVersion = "1000.0.0";

        var message = Assert.Single(ResolveExpectingOnly(model, 126));

        Assert.Contains("1000.0.0", message.MessageText);
    }

    [Fact]
    public void CurrentEngine_ReadsCkV2Models()
    {
        var model = Model();
        model.MinEngineVersion = CkEngineVersion.CkV2MinEngineVersion;

        ResolveExpectingNoMessages(model);
    }

    [Fact]
    public void CheckModel_ReportsTheRunningVersion()
    {
        var model = Model();
        model.MinEngineVersion = "3.5.0";
        var operationResult = new OperationResult();

        Assert.False(CkEngineVersion.CheckModel(model, "loc", operationResult, new Version(3, 4, 149)));

        var message = Assert.Single(operationResult.Messages);
        Assert.Equal(126, message.MessageNumber);
        Assert.Contains("3.4.149", message.MessageText);
        Assert.True(CkEngineVersion.CheckModel(new CkCompiledModelRoot { ModelId = model.ModelId }, "loc",
            new OperationResult(), new Version(0, 0, 1)));
    }
}
