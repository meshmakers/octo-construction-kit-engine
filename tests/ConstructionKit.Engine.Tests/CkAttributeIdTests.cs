
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests;

public class CkAttributeIdTests
{
    [Fact]
    public void Create_CkAttributeId_Complete()
    {
        var attributeId = new CkAttributeId("Designation-1.0.0");
        Assert.Equal("Designation", attributeId.AttributeId);
        Assert.Equal("1.0.0", attributeId.Version);
    }
    
    [Fact]
    public void Create_CkAttributeId_WithoutVersion()
    {
        var attributeId = new CkAttributeId("Designation");
        Assert.Equal("Designation", attributeId.AttributeId);
        Assert.Equal("1.0.0", attributeId.Version);
    }
    
    [Fact]
    public void Create_CkAttributeId_FromString()
    {
        CkAttributeId attributeId = "Designation-1.0.0";
        Assert.Equal("Designation", attributeId.AttributeId);
        Assert.Equal("1.0.0", attributeId.Version);
    }
    
    [Fact]
    public void Create_CkAttributeId_Malformed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CkAttributeId(""));
    }
    
    [Fact]
    public void Compare_CkAttributeId_Same()
    {
        var id1 = new CkAttributeId("Designation-1.0.0");
        var id2 = new CkAttributeId("Designation-1.0.0");
        Assert.Equal(0, id1.CompareTo(id2));
    }

    [Fact]
    public void Compare_CkAttributeId_HigherVersion()
    {
        var id1 = new CkAttributeId("Designation-1.0.0");
        var id2 = new CkAttributeId("Designation-1.0.1");
        Assert.Equal(-1, id1.CompareTo(id2));
    }
    
    [Fact]
    public void Compare_CkAttributeId_LowerVersionVersion()
    {
        var id1 = new CkAttributeId("Designation-1.0.1");
        var id2 = new CkAttributeId("Designation-1.0.0");
        Assert.Equal(1, id1.CompareTo(id2));
    }
    
    [Fact]
    public void Compare_CkAttributeId_DifferentTypeId()
    {
        var id1 = new CkAttributeId("TestEntity1-1.0.0");
        var id2 = new CkAttributeId("TestEntity2-1.0.0");
        Assert.Equal(-1, id1.CompareTo(id2));
    }
    
    [Fact]
    public void Equal_CkAttributeId_Same()
    {
        var id1 = new CkAttributeId("Designation-1.0.0");
        var id2 = new CkAttributeId("Designation-1.0.0");
        Assert.True(id1.Equals(id2));
    }
    
    [Fact]
    public void Equal_CkAttributeId_DifferentVersion()
    {
        var id1 = new CkAttributeId("TestEntity-1.0.0");
        var id2 = new CkAttributeId("TestEntity-1.0.1");
        Assert.False(id1.Equals(id2));
    }
    
    [Fact]
    public void Equal_CkAttributeId_DifferentTypeId()
    {
        var id1 = new CkAttributeId("Designation-1.0.0");
        var id2 = new CkAttributeId("Description-1.0.0");
        Assert.False(id1.Equals(id2));
    }

}