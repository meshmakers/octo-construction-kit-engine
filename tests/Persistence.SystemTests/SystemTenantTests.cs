using Meshmakers.Octo.SystematizedData.Persistence.SystemTests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Meshmakers.Octo.SystematizedData.Persistence.SystemTests;

public class SystemTenantTests : IClassFixture<SystemFixture>
{
    private readonly SystemFixture _systemFixture;
    private readonly ITestOutputHelper _testOutputHelper;

    public SystemTenantTests(SystemFixture systemFixture, ITestOutputHelper testOutputHelper)
    {
        _systemFixture = systemFixture;
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async void IsSystemTenantExisting()
    {
        var systemContext = _systemFixture.GetSystemContext();
        var result = await systemContext.IsSystemTenantExistingAsync();
        Assert.True(result);
    }
    
    [Fact]
    public async void CreateChildTenantAndDeleteAsync()
    {
        var systemContext = _systemFixture.GetSystemContext();
        using var session = await systemContext.GetSystemSessionAsync();
        session.StartTransaction();
        await systemContext.CreateChildTenantAsync(session, "TestTenant", "TestTenant");

        await session.CommitTransactionAsync();

        using var session2 = await systemContext.GetSystemSessionAsync();
        session2.StartTransaction();
        var r = await systemContext.GetChildTenantAsync(session2, "TestTenant");
        await session2.CommitTransactionAsync();

        Assert.Equal("testtenant", r.TenantId);
        Assert.Equal("testtenant", r.DatabaseName);

        using var session3 = await systemContext.GetSystemSessionAsync();
        session3.StartTransaction();

        await systemContext.DropChildTenantAsync(session3, "TestTenant");

        await session3.CommitTransactionAsync();

        using var session4 = await systemContext.GetSystemSessionAsync();
        session4.StartTransaction();
        var r2 = await systemContext.IsChildTenantExistingAsync(session4, "TestTenant");
        await session4.CommitTransactionAsync();

        Assert.False(r2);
    }
    
    [Fact]
    public async void CreateTwoChildTenantsAndDeleteAsync()
    {
        // Create child tenant "Father" form octo system
        var systemContext = _systemFixture.GetSystemContext();
        using (var session = await systemContext.GetSystemSessionAsync())
        {
            session.StartTransaction();
            
            await systemContext.CreateChildTenantAsync(session, "Father", "Father");
            await session.CommitTransactionAsync();
        }
        var fatherTenantContext = await systemContext.GetChildTenantContextAsync("Father");

        // Create child tenant "Girl" from child tenant "Father"
        using (var session = await systemContext.GetSystemSessionAsync())
        {
            session.StartTransaction();

            await fatherTenantContext.CreateChildTenantAsync(session, "Girl", "Girl");
            await session.CommitTransactionAsync();
        }

        // Check if tenant Girl Exists
        using (var session = await systemContext.GetSystemSessionAsync())
        {
            session.StartTransaction();

            Assert.True(await systemContext.IsChildTenantExistingAsync(session, "Father"));
            Assert.True(await systemContext.IsChildTenantExistingAsync(session, "Girl"));


            await session.CommitTransactionAsync();
        }

        // Drop childs
        using (var session = await systemContext.GetSystemSessionAsync())
        {
            session.StartTransaction();

            await fatherTenantContext.DropChildTenantAsync(session, "Girl");

            await session.CommitTransactionAsync();
        }
        
        using (var session = await systemContext.GetSystemSessionAsync())
        {
            session.StartTransaction();

            await systemContext.DropChildTenantAsync(session, "Father");

            await session.CommitTransactionAsync();
        }

    }
    
    
    [Fact]
    public async void AttachAndDetachTenant()
    {
        // Create child tenant "Father" form octo system
        var systemContext = _systemFixture.GetSystemContext();
        using (var session = await systemContext.GetSystemSessionAsync())
        {
            session.StartTransaction();
            
            await systemContext.CreateChildTenantAsync(session, "Father", "Father");
            await session.CommitTransactionAsync();
        }

        // Detach Father
        using (var session = await systemContext.GetSystemSessionAsync())
        {
            session.StartTransaction();

            await systemContext.DetachChildTenantAsync(session, "Father");
            await session.CommitTransactionAsync();
        }

        // Attach Father
        using (var session = await systemContext.GetSystemSessionAsync())
        {
            session.StartTransaction();

            await systemContext.AttachChildTenantAsync(session, "Father", "Father");
            await session.CommitTransactionAsync();
        }
        
        // Drop
        using (var session = await systemContext.GetSystemSessionAsync())
        {
            session.StartTransaction();

            await systemContext.DropChildTenantAsync(session, "Father");

            await session.CommitTransactionAsync();
        }
    }
}