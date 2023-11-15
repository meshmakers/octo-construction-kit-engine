using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.SystematizedData.Persistence.SystemTests.CkTest.ConstructionKit.Generated.Test.v1;
using Meshmakers.Octo.SystematizedData.Persistence.SystemTests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Meshmakers.Octo.SystematizedData.Persistence.SystemTests;

public class BasicRtEntityTests: IClassFixture<SystemFixture>
{
    private readonly SystemFixture _systemFixture;
    private readonly ITestOutputHelper _testOutputHelper;

    public BasicRtEntityTests(SystemFixture systemFixture, ITestOutputHelper testOutputHelper)
    {
        _systemFixture = systemFixture;
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async void CreateRtEntity()
    {
        var systemContext = _systemFixture.GetSystemContext();
        
        using var systemSession = await systemContext.GetSystemSessionAsync();
        systemSession.StartTransaction();

        OperationResult operationResult = new();
        await systemContext.ImportCkModelAsync(systemSession, new CkModelId("System.Identity-1.0.0"), operationResult);
        await systemSession.CommitTransactionAsync();

        var tenantRepository = await systemContext.GetTenantRepositoryAsync();

        using var session = await tenantRepository.GetSessionAsync();
        session.StartTransaction();

        try
        {
            var x = tenantRepository.CreateTransientRtEntity<RtPlanet>();
            x.Designation = "test";
            await tenantRepository.InsertOneRtEntityAsync(session, x);

            await session.CommitTransactionAsync();  
        }
        catch (Exception e)
        {
            _testOutputHelper.WriteLine(e.ToString());
            await session.AbortTransactionAsync();
            throw;
        }

    }
}