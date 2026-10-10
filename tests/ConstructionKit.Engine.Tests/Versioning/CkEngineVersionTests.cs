using System.Reflection;
using System.Reflection.Emit;
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
    // AB#6390: the CK v2 minimum is patch-exact 3.5.1.
    [InlineData("3.5.1", "3.4.149", false)]
    [InlineData("3.5.1", "3.5.0", false)]
    [InlineData("3.5.1", "3.5.1", true)]
    [InlineData("3.5.1", "3.5.2", true)]
    [InlineData("3.5.1", "3.6.0", true)]
    [InlineData("3.5.1", "999.0.0", true)]
    [InlineData("1000.0.0", "999.0.0", false)]
    // Main line (< 1.0) skips the check by rule, even for a model that requires far more.
    [InlineData("3.5.1", "0.1.0", true)]
    [InlineData("1000.0.0", "0.1.0", true)]
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

    [Fact]
    public void CkV2MinEngineVersion_Is351()
    {
        Assert.Equal("3.5.1", CkEngineVersion.CkV2MinEngineVersion);
    }

    [Theory]
    [InlineData("3.4.149", false)]
    [InlineData("3.5.0", false)]
    [InlineData("3.5.1", true)]
    [InlineData("3.5.2", true)]
    [InlineData("3.6.0", true)]
    [InlineData("999.0.0", true)]
    [InlineData("0.1.2610", true)] // main-line rule: major < 1 skips the check
    public void Code126_ForACkV2Model_ByRunningEngine(string engine, bool accepted)
    {
        using var _ = CkEngineVersion.OverrideCurrentForTests(Version.Parse(engine));
        var model = Model();
        model.MinEngineVersion = CkEngineVersion.CkV2MinEngineVersion;

        if (accepted)
        {
            ResolveExpectingNoMessages(model);
            return;
        }

        var message = Assert.Single(ResolveExpectingOnly(model, 126));
        Assert.Contains("3.5.1", message.MessageText);
        Assert.Contains(Version.Parse(engine).ToString(3), message.MessageText);
    }

    [Theory]
    [InlineData("3.5.2.0", "3.5.2")]
    [InlineData("3.5.2", "3.5.2")]
    [InlineData("3.5.2-rc1", "3.5.2")]
    [InlineData("3.5.2.0+0123abc", "3.5.2")]
    [InlineData("3.5.2.7-feature-x+0123abc", "3.5.2")]
    [InlineData("0.1.2610.9010", "0.1.2610")]
    [InlineData("3.5", "3.5.0")]
    [InlineData("not-a-version", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void TryParseEngineVersion(string? text, string? expected)
    {
        Assert.Equal(expected, CkEngineVersion.TryParseEngineVersion(text)?.ToString());
    }

    [Fact]
    public void ReadRunningVersion_IsPatchExact_AssemblyVersionIsStampedAsMajorMinorZero()
    {
        // What set-version.yml stamps on an r3.5.2 build: AssemblyVersion 3.5.0.0, FileVersion/Informational 3.5.2.0.
        var assembly = BuildAssembly("3.5.0.0", "3.5.2.0", "3.5.2.0");

        Assert.Equal(new Version(3, 5, 2), CkEngineVersion.ReadRunningVersion(assembly));
    }

    [Fact]
    public void ReadRunningVersion_PrefersTheFileVersion_AndStripsSuffixesOfTheInformationalVersion()
    {
        Assert.Equal(new Version(3, 5, 2),
            CkEngineVersion.ReadRunningVersion(BuildAssembly("3.5.0.0", "3.5.2.0", "3.5.9.0-rc1+sha")));
        Assert.Equal(new Version(3, 5, 2),
            CkEngineVersion.ReadRunningVersion(BuildAssembly("3.5.0.0", null, "3.5.2.7-rc1+sha")));
    }

    [Fact]
    public void ReadRunningVersion_UnparsableVersions_FallBackToTheAssemblyVersionAndDoNotThrow()
    {
        Assert.Equal(new Version(3, 5, 0),
            CkEngineVersion.ReadRunningVersion(BuildAssembly("3.5.0.0", "garbage", "also garbage")));
        Assert.Equal(new Version(3, 5, 0), CkEngineVersion.ReadRunningVersion(BuildAssembly("3.5.0.0", null, null)));
        Assert.Equal(new Version(3, 5, 1),
            CkEngineVersion.ReadRunningVersion(BuildAssembly("3.5.0.0", "garbage", "3.5.1.0")));
    }

    [Theory]
    [InlineData("0.1.0.0", "0.1.2610.9010")] // main-line private feed build
    [InlineData("0.1.0.0", null)]
    public void ReadRunningVersion_MainLine_IsNullAndSkipsTheCheck(string assemblyVersion, string? fileVersion)
    {
        var assembly = BuildAssembly(assemblyVersion, fileVersion, fileVersion);

        Assert.Null(CkEngineVersion.ReadRunningVersion(assembly));
        Assert.True(CkEngineVersion.IsSatisfiedBy("1000.0.0", CkEngineVersion.ReadRunningVersion(assembly)));
    }

    [Fact]
    public void ReadRunningVersion_DebugL_999_AcceptsEveryModelButNotTheFuture()
    {
        var debugL = CkEngineVersion.ReadRunningVersion(BuildAssembly("999.0.0.0", "999.0.0.0", "999.0.0"));

        Assert.Equal(new Version(999, 0, 0), debugL);
        Assert.True(CkEngineVersion.IsSatisfiedBy("3.5.1", debugL));
        Assert.False(CkEngineVersion.IsSatisfiedBy("1000.0.0", debugL));
    }

    [Fact]
    public void OverrideCurrentForTests_PinsAndRestoresCurrent()
    {
        var ambient = CkEngineVersion.Current;
        using (CkEngineVersion.OverrideCurrentForTests(new Version(3, 5, 0, 7)))
        {
            Assert.Equal(new Version(3, 5, 0), CkEngineVersion.Current);
        }

        Assert.Equal(ambient, CkEngineVersion.Current);
    }

    private static Assembly BuildAssembly(string assemblyVersion, string? fileVersion, string? informationalVersion)
    {
        var builder = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("EngineVersionStub" + Guid.NewGuid().ToString("N")) { Version = Version.Parse(assemblyVersion) },
            AssemblyBuilderAccess.Run);
        if (fileVersion != null)
        {
            builder.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(AssemblyFileVersionAttribute).GetConstructor([typeof(string)])!, [fileVersion]));
        }

        if (informationalVersion != null)
        {
            builder.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!,
                [informationalVersion]));
        }

        return builder;
    }
}
