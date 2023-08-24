using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

public class CkVersionTests
{
    [Fact]
    public void Create_CkTypeId_Complete()
    {
        var ckVersion = new CkVersion("1.0.0");
        Assert.Equal(1, ckVersion.Major);
        Assert.Equal(0, ckVersion.Minor);
        Assert.Equal(0, ckVersion.Revision);
    }
}