using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests;

public class CkIdTests
{
    [Fact]
    public void Copy_CkId()
    {
        var ckTypeId = new CkId<CkTypeId>("System/Designation-1.0.0");
        var ckTypeId2 = ckTypeId;
        test(ckTypeId2);
    }

    private void test(CkId<CkTypeId> ckTypeId)
    {
        Assert.Equal("System", ckTypeId.ModelId.ModelId);
        Assert.Equal("Designation", ckTypeId.Key.TypeId);
        Assert.Equal("1.0.0", ckTypeId.ModelId.ModelVersion);
        Assert.Equal("1.0.0", ckTypeId.Key.Version);
    }
}