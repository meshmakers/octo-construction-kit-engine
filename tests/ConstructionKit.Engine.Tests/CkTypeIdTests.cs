using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests;

public class CkTypeIdTests
{
    [Fact]
    public void Create_CkTypeId_Complete()
    {
        var ckTypeId = new CkTypeId("TestEntity-1");
        Assert.Equal("TestEntity", ckTypeId.Name);
        Assert.Equal(1u, ckTypeId.Version);
    }

    [Fact]
    public void Create_CkTypeId_WithoutVersion()
    {
        var ckTypeId = new CkTypeId("TestEntity");
        Assert.Equal("TestEntity", ckTypeId.Name);
        Assert.Equal(1u, ckTypeId.Version);
    }

    [Fact]
    public void Create_CkTypeId_FromString()
    {
        CkTypeId ckTypeId = "TestEntity-1";
        Assert.Equal("TestEntity", ckTypeId.Name);
        Assert.Equal(1u, ckTypeId.Version);
    }

    [Fact]
    public void Create_CkTypeId_Malformed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CkTypeId(""));
    }

    [Fact]
    public void Compare_CkTypeId_Same()
    {
        var ckTypeId1 = new CkTypeId("System/TestEntity-1");
        var ckTypeId2 = new CkTypeId("System/TestEntity-1");
        Assert.Equal(0, ckTypeId1.CompareTo(ckTypeId2));
    }

    [Fact]
    public void Compare_CkTypeId_HigherVersion()
    {
        var ckTypeId1 = new CkTypeId("System/TestEntity-1");
        var ckTypeId2 = new CkTypeId("System/TestEntity-2");
        Assert.Equal(-1, ckTypeId1.CompareTo(ckTypeId2));
    }

    [Fact]
    public void Compare_CkTypeId_LowerVersionVersion()
    {
        var ckTypeId1 = new CkTypeId("System/TestEntity-2");
        var ckTypeId2 = new CkTypeId("System/TestEntity-1");
        Assert.Equal(1, ckTypeId1.CompareTo(ckTypeId2));
    }

    [Fact]
    public void Compare_CkTypeId_DifferentTypeId()
    {
        var ckTypeId1 = new CkTypeId("TestEntity1-1");
        var ckTypeId2 = new CkTypeId("TestEntity2-1");
        Assert.Equal(-1, ckTypeId1.CompareTo(ckTypeId2));
    }

    [Fact]
    public void Equal_CkTypeId_Same()
    {
        var ckTypeId1 = new CkTypeId("TestEntity-1");
        var ckTypeId2 = new CkTypeId("TestEntity-1");
        Assert.True(ckTypeId1.Equals(ckTypeId2));
    }

    [Fact]
    public void Equal_CkTypeId_DifferentVersion()
    {
        var ckTypeId1 = new CkTypeId("TestEntity-1");
        var ckTypeId2 = new CkTypeId("TestEntity-2");
        Assert.False(ckTypeId1.Equals(ckTypeId2));
    }

    [Fact]
    public void Equal_CkTypeId_DifferentTypeId()
    {
        var ckTypeId1 = new CkTypeId("TestEntity1-1");
        var ckTypeId2 = new CkTypeId("TestEntity2-1");
        Assert.False(ckTypeId1.Equals(ckTypeId2));
    }
}