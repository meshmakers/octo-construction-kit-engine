using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests;

public class CkIdTests
{
    [Fact]
    public void Copy_CkId()
    {
        var ckTypeId = new CkId<CkTypeId>("System/Designation-1");
        var ckTypeId2 = ckTypeId;
        Assert.Equal("System", ckTypeId2.ModelId.Name);
        Assert.Equal("Designation", ckTypeId2.ElementId.Name);
        Assert.Equal("1.0.0", ckTypeId2.ModelId.Version);
        Assert.Equal(1u, ckTypeId2.ElementId.Version);
    }
}