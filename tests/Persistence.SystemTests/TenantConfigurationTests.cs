using Meshmakers.Octo.SystematizedData.Persistence.SystemTests.Fixtures;
using Xunit;

namespace Meshmakers.Octo.SystematizedData.Persistence.SystemTests;

public class TenantConfigurationTests: IClassFixture<SystemFixture>
{
    private readonly SystemFixture _systemFixture;

    public TenantConfigurationTests(SystemFixture systemFixture)
    {
        _systemFixture = systemFixture;
    }

    private class TestClass
    {
        public string TestProperty { get; init; } = string.Empty;
    }

    [Fact]
    public async void SetGetConfigurationAsString()
    {
        var systemContext = _systemFixture.GetSystemContext();
        using var session = await systemContext.GetSystemSessionAsync();
        session.StartTransaction();
        await systemContext.SetConfigurationAsync(session, "test", "398FE06C-4E21-433F-A8EA-1BDD74E1B167");

        await session.CommitTransactionAsync();
        
        using var session2 = await systemContext.GetSystemSessionAsync();
        session2.StartTransaction();
        
        var r = await systemContext.GetConfigurationAsync(session2, "test");
        
        await session2.CommitTransactionAsync();
        
        Assert.Equal("398FE06C-4E21-433F-A8EA-1BDD74E1B167", r);
    }
    
    [Fact]
    public async void SetGetConfigurationAsObject()
    {
        var systemContext = _systemFixture.GetSystemContext();
        using var session = await systemContext.GetSystemSessionAsync();
        session.StartTransaction();
        await systemContext.SetConfigurationAsync(session, "test", new TestClass { TestProperty = "398FE06C-4E21-433F-A8EA-1BDD74E1B168" });

        await session.CommitTransactionAsync();
        
        using var session2 = await systemContext.GetSystemSessionAsync();
        session2.StartTransaction();
        
        var r = await systemContext.GetConfigurationAsync<TestClass>(session2, "test", null);
        
        await session2.CommitTransactionAsync();
        
        Assert.Equal("398FE06C-4E21-433F-A8EA-1BDD74E1B168", r?.TestProperty);
    }
    
    [Fact]
    public async void GetConfigurationObject_NoKey()
    {
        var systemContext = _systemFixture.GetSystemContext();
        using var session2 = await systemContext.GetSystemSessionAsync();
        session2.StartTransaction();
        
        var r = await systemContext.GetConfigurationAsync<TestClass>(session2, "B227CD7A-6BEE-499A-A888-2961E0D06545", null);
        
        await session2.CommitTransactionAsync();
        
        Assert.Null(r);
    }
    
    [Fact]
    public async void GetConfigurationString_NoKey()
    {
        var systemContext = _systemFixture.GetSystemContext();
        using var session2 = await systemContext.GetSystemSessionAsync();
        session2.StartTransaction();
        
        var r = await systemContext.GetConfigurationAsync(session2, "B227CD7A-6BEE-499A-A888-2961E0D06546");
        
        await session2.CommitTransactionAsync();
        
        Assert.Null(r);
    }
}