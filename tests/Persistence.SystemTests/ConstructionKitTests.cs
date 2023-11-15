using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.SystematizedData.Persistence.SystemTests.Fixtures;
using Xunit;

namespace Meshmakers.Octo.SystematizedData.Persistence.SystemTests;

public class ConstructionKitTests : IClassFixture<SystemFixture>
{
    private readonly SystemFixture _systemFixture;

    public ConstructionKitTests(SystemFixture systemFixture)
    {
        _systemFixture = systemFixture;
    }
    
    [Fact]
    public async void ImportConstructionKit()
    {
        var systemContext = _systemFixture.GetSystemContext();

        using var session = await systemContext.GetSystemSessionAsync();
        session.StartTransaction();

        var operationResult = new OperationResult();
        await systemContext.ImportCkModelAsync(session, new CkModelId("Test-1.0.0"), operationResult);
        await systemContext.ImportCkModelAsync(session, new CkModelId("System.Identity-1.0.0"), operationResult);

        await session.CommitTransactionAsync();
        
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }


}
