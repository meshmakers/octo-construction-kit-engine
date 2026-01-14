using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Meshmakers.Octo.Runtime.Engine.Blueprints;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Blueprints;

public class InMemoryTenantBlueprintHistoryTests
{
    private readonly InMemoryTenantBlueprintHistory _sut;

    public InMemoryTenantBlueprintHistoryTests()
    {
        _sut = new InMemoryTenantBlueprintHistory();
    }

    private static TenantBlueprintInfo CreateBlueprintInfo(
        string name,
        string version,
        BlueprintApplicationMode mode = BlueprintApplicationMode.Initial)
    {
        return new TenantBlueprintInfo
        {
            BlueprintId = new BlueprintId($"{name}-{version}"),
            AppliedAt = DateTime.UtcNow,
            ApplicationMode = mode,
            EntitiesCreated = 10,
            EntitiesUpdated = 5,
            EntitiesDeleted = 2
        };
    }

    [Fact]
    public async Task GetHistoryAsync_ShouldReturnEmptyListForUnknownTenant()
    {
        // Arrange
        var tenantId = "unknown-tenant";
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.GetHistoryAsync(tenantId, ct);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task AddEntryAsync_ShouldAddEntryToHistory()
    {
        // Arrange
        var tenantId = "test-tenant-add";
        var info = CreateBlueprintInfo("MyBlueprint", "1.0.0");
        var ct = TestContext.Current.CancellationToken;

        // Act
        await _sut.AddEntryAsync(tenantId, info, ct);
        var history = await _sut.GetHistoryAsync(tenantId, ct);

        // Assert
        Assert.Single(history);
        Assert.Equal(info.BlueprintId.FullName, history[0].BlueprintId.FullName);
    }

    [Fact]
    public async Task AddEntryAsync_ShouldAddMultipleEntries()
    {
        // Arrange
        var tenantId = "test-tenant-multi";
        var ct = TestContext.Current.CancellationToken;
        var info1 = CreateBlueprintInfo("MyBlueprint", "1.0.0");
        await Task.Delay(10, ct);
        var info2 = CreateBlueprintInfo("MyBlueprint", "1.1.0", BlueprintApplicationMode.Update);
        await Task.Delay(10, ct);
        var info3 = CreateBlueprintInfo("MyBlueprint", "2.0.0", BlueprintApplicationMode.Migration);

        // Act
        await _sut.AddEntryAsync(tenantId, info1, ct);
        await _sut.AddEntryAsync(tenantId, info2, ct);
        await _sut.AddEntryAsync(tenantId, info3, ct);
        var history = await _sut.GetHistoryAsync(tenantId, ct);

        // Assert
        Assert.Equal(3, history.Count);
    }

    [Fact]
    public async Task GetHistoryAsync_ShouldReturnEntriesInChronologicalOrder()
    {
        // Arrange
        var tenantId = "test-tenant-order";
        var ct = TestContext.Current.CancellationToken;

        var info1 = new TenantBlueprintInfo
        {
            BlueprintId = new BlueprintId("MyBlueprint-1.0.0"),
            AppliedAt = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            ApplicationMode = BlueprintApplicationMode.Initial
        };

        var info2 = new TenantBlueprintInfo
        {
            BlueprintId = new BlueprintId("MyBlueprint-1.1.0"),
            AppliedAt = new DateTime(2024, 2, 1, 10, 0, 0, DateTimeKind.Utc),
            ApplicationMode = BlueprintApplicationMode.Update
        };

        var info3 = new TenantBlueprintInfo
        {
            BlueprintId = new BlueprintId("MyBlueprint-2.0.0"),
            AppliedAt = new DateTime(2024, 3, 1, 10, 0, 0, DateTimeKind.Utc),
            ApplicationMode = BlueprintApplicationMode.Migration
        };

        // Add in non-chronological order
        await _sut.AddEntryAsync(tenantId, info2, ct);
        await _sut.AddEntryAsync(tenantId, info1, ct);
        await _sut.AddEntryAsync(tenantId, info3, ct);

        // Act
        var history = await _sut.GetHistoryAsync(tenantId, ct);

        // Assert
        Assert.Equal(3, history.Count);
        Assert.Equal("MyBlueprint-1.0.0", history[0].BlueprintId.FullName);
        Assert.Equal("MyBlueprint-1.1.0", history[1].BlueprintId.FullName);
        Assert.Equal("MyBlueprint-2.0.0", history[2].BlueprintId.FullName);
    }

    [Fact]
    public async Task GetCurrentAsync_ShouldReturnNullForUnknownTenant()
    {
        // Arrange
        var tenantId = "unknown-tenant";
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.GetCurrentAsync(tenantId, ct);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetCurrentAsync_ShouldReturnMostRecentEntry()
    {
        // Arrange
        var tenantId = "test-tenant-current";
        var ct = TestContext.Current.CancellationToken;

        var info1 = new TenantBlueprintInfo
        {
            BlueprintId = new BlueprintId("MyBlueprint-1.0.0"),
            AppliedAt = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            ApplicationMode = BlueprintApplicationMode.Initial
        };

        var info2 = new TenantBlueprintInfo
        {
            BlueprintId = new BlueprintId("MyBlueprint-2.0.0"),
            AppliedAt = new DateTime(2024, 3, 1, 10, 0, 0, DateTimeKind.Utc),
            ApplicationMode = BlueprintApplicationMode.Update
        };

        var info3 = new TenantBlueprintInfo
        {
            BlueprintId = new BlueprintId("MyBlueprint-1.5.0"),
            AppliedAt = new DateTime(2024, 2, 1, 10, 0, 0, DateTimeKind.Utc),
            ApplicationMode = BlueprintApplicationMode.Update
        };

        // Add in any order
        await _sut.AddEntryAsync(tenantId, info1, ct);
        await _sut.AddEntryAsync(tenantId, info3, ct);
        await _sut.AddEntryAsync(tenantId, info2, ct);

        // Act
        var current = await _sut.GetCurrentAsync(tenantId, ct);

        // Assert
        Assert.NotNull(current);
        Assert.Equal("MyBlueprint-2.0.0", current.BlueprintId.FullName);
    }

    [Fact]
    public async Task HasBlueprintAsync_ShouldReturnFalseForUnknownTenant()
    {
        // Arrange
        var tenantId = "unknown-tenant";
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.HasBlueprintAsync(tenantId, ct);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HasBlueprintAsync_ShouldReturnTrueForTenantWithBlueprint()
    {
        // Arrange
        var tenantId = "test-tenant-has";
        var info = CreateBlueprintInfo("MyBlueprint", "1.0.0");
        var ct = TestContext.Current.CancellationToken;
        await _sut.AddEntryAsync(tenantId, info, ct);

        // Act
        var result = await _sut.HasBlueprintAsync(tenantId, ct);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task DifferentTenants_ShouldHaveIsolatedHistories()
    {
        // Arrange
        var tenant1 = "tenant-1";
        var tenant2 = "tenant-2";
        var ct = TestContext.Current.CancellationToken;

        var info1 = CreateBlueprintInfo("Blueprint1", "1.0.0");
        var info2 = CreateBlueprintInfo("Blueprint2", "2.0.0");

        await _sut.AddEntryAsync(tenant1, info1, ct);
        await _sut.AddEntryAsync(tenant2, info2, ct);

        // Act
        var history1 = await _sut.GetHistoryAsync(tenant1, ct);
        var history2 = await _sut.GetHistoryAsync(tenant2, ct);
        var current1 = await _sut.GetCurrentAsync(tenant1, ct);
        var current2 = await _sut.GetCurrentAsync(tenant2, ct);

        // Assert
        Assert.Single(history1);
        Assert.Single(history2);
        Assert.Equal("Blueprint1-1.0.0", history1[0].BlueprintId.FullName);
        Assert.Equal("Blueprint2-2.0.0", history2[0].BlueprintId.FullName);
        Assert.Equal("Blueprint1-1.0.0", current1?.BlueprintId.FullName);
        Assert.Equal("Blueprint2-2.0.0", current2?.BlueprintId.FullName);
    }

    [Fact]
    public async Task AddEntryAsync_ShouldPreserveAllProperties()
    {
        // Arrange
        var tenantId = "test-tenant-props";
        var ct = TestContext.Current.CancellationToken;
        var previousVersion = new BlueprintId("MyBlueprint-0.9.0");
        var info = new TenantBlueprintInfo
        {
            BlueprintId = new BlueprintId("MyBlueprint-1.0.0"),
            AppliedAt = new DateTime(2024, 5, 15, 14, 30, 0, DateTimeKind.Utc),
            ApplicationMode = BlueprintApplicationMode.Update,
            PreviousVersion = previousVersion,
            EntitiesCreated = 25,
            EntitiesUpdated = 10,
            EntitiesDeleted = 3,
            AppliedBy = "admin@example.com",
            Notes = "Production rollout"
        };

        // Act
        await _sut.AddEntryAsync(tenantId, info, ct);
        var history = await _sut.GetHistoryAsync(tenantId, ct);

        // Assert
        Assert.Single(history);
        var retrieved = history[0];
        Assert.Equal("MyBlueprint-1.0.0", retrieved.BlueprintId.FullName);
        Assert.Equal(new DateTime(2024, 5, 15, 14, 30, 0, DateTimeKind.Utc), retrieved.AppliedAt);
        Assert.Equal(BlueprintApplicationMode.Update, retrieved.ApplicationMode);
        Assert.NotNull(retrieved.PreviousVersion);
        Assert.Equal("MyBlueprint-0.9.0", retrieved.PreviousVersion.FullName);
        Assert.Equal(25, retrieved.EntitiesCreated);
        Assert.Equal(10, retrieved.EntitiesUpdated);
        Assert.Equal(3, retrieved.EntitiesDeleted);
        Assert.Equal("admin@example.com", retrieved.AppliedBy);
        Assert.Equal("Production rollout", retrieved.Notes);
    }
}
