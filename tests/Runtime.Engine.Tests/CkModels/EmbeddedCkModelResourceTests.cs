using System.Reflection;
using Meshmakers.Octo.ConstructionKit.Models.System.Files.Generated.System.Files.v1;

namespace Meshmakers.Octo.Runtime.Engine.Tests.CkModels;

/// <summary>
/// Guards the CK model assemblies against shipping without their compiled model. CI build 49921 published
/// Meshmakers.Octo.ConstructionKit.Models.StreamData 0.1.2610.6005 without ck-system.streamdata.yaml because the
/// resource was added after the SDK had computed the manifest resources; every consumer then failed with
/// "'...ck-system.streamdata.yaml' not found in resources".
/// </summary>
public class EmbeddedCkModelResourceTests
{
    [Theory]
    [InlineData("Meshmakers.Octo.ConstructionKit.Models.System", "Meshmakers.Octo.ConstructionKit.Models.System.ck-system-2.yaml")]
    [InlineData("Meshmakers.Octo.ConstructionKit.Models.StreamData", "Meshmakers.Octo.ConstructionKit.Models.StreamData.ck-system.streamdata.yaml")]
    [InlineData("Meshmakers.Octo.ConstructionKit.Models.System.Files", "Meshmakers.Octo.ConstructionKit.Models.System.Files.ck-system.files.yaml")]
    [InlineData("TestCkModel", "TestCkModel.ck-test.yaml")]
    public void CkModelAssembly_EmbedsItsCompiledModel(string assemblyName, string expectedResource)
    {
        var assembly = Assembly.Load(assemblyName);

        Assert.Contains(expectedResource, assembly.GetManifestResourceNames());

        using var stream = assembly.GetManifestResourceStream(expectedResource);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();
        Assert.Contains("modelId", content, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// AB#6479: System.Files moved from the asset repository into this repository without a model change.
    /// AB#6691: 1.1.0 is the one-time MINOR bump forced by range retention becoming the compiler default (SemVer
    /// row D6, OCTO-CK100): same content, dependency pinned as a range. A further bump is a deliberate change of
    /// this model and must update this assertion.
    /// </summary>
    [Fact]
    public void SystemFilesCkModel_HasModelId_1_1_0()
    {
        Assert.Equal("System.Files-1.1.0", SystemFilesCkIds.CkModelId.ToString());
    }
}
