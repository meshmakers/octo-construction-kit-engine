using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests;

public class CkModelIdTest
{
    [Fact]
    public void Create_CkModelId_Complete()
    {
        var modelId = new CkModelId("System-1.0.0");
        Assert.Equal("System", modelId.ModelId);
        Assert.Equal("1.0.0", modelId.ModelVersion);
    }

    [Fact]
    public void Create_CkModelId_WithoutVersion()
    {
        var modelId = new CkModelId("System");
        Assert.Equal("System", modelId.ModelId);
        Assert.Equal("1.0.0", modelId.ModelVersion);
    }

    [Fact]
    public void Create_CkModelId_FromString()
    {
        CkModelId modelId = "System-1.0.0";
        Assert.Equal("System", modelId.ModelId);
        Assert.Equal("1.0.0", modelId.ModelVersion);
    }

    [Fact]
    public void Create_CkModelId_Malformed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CkModelId("System-"));
    }

    [Fact]
    public void Compare_CkModelId_Same()
    {
        var id1 = new CkModelId("System-1.0.0");
        var id2 = new CkModelId("System-1.0.0");
        Assert.Equal(0, id1.CompareTo(id2));
    }

    [Fact]
    public void Compare_CkModelId_HigherVersion()
    {
        var id1 = new CkModelId("System-1.0.0");
        var id2 = new CkModelId("System-1.0.1");
        Assert.Equal(-1, id1.CompareTo(id2));
    }

    [Fact]
    public void Compare_CkModelId_LowerVersionVersion()
    {
        var id1 = new CkModelId("System-1.0.1");
        var id2 = new CkModelId("System-1.0.0");
        Assert.Equal(1, id1.CompareTo(id2));
    }

    [Fact]
    public void Compare_CkModelId_DifferentModelId()
    {
        var id1 = new CkModelId("System1-1.0.0");
        var id2 = new CkModelId("System2-1.0.0");
        Assert.Equal(-1, id1.CompareTo(id2));
    }


    [Fact]
    public void Equal_CkModelId_DifferentVersion()
    {
        var id1 = new CkModelId("System-1.0.0");
        var id2 = new CkModelId("System-1.0.1");
        Assert.True(id1.Equals(id2));
    }
}