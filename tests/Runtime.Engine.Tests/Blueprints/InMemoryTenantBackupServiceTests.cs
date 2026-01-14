using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Meshmakers.Octo.Runtime.Engine.Blueprints;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Blueprints;

public class InMemoryTenantBackupServiceTests
{
    private readonly InMemoryTenantBackupService _sut;

    public InMemoryTenantBackupServiceTests()
    {
        _sut = new InMemoryTenantBackupService(NullLogger<InMemoryTenantBackupService>.Instance);
    }

    [Fact]
    public async Task CreateBackupAsync_ShouldCreateBackupWithUniqueId()
    {
        // Arrange
        var tenantId = "test-tenant";
        var reason = "Before update to MyBlueprint-2.0.0";
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.CreateBackupAsync(tenantId, reason, ct);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.BackupId);
        Assert.StartsWith("backup-", result.BackupId);
        Assert.Equal(tenantId, result.TenantId);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(BackupType.BlueprintUpdate, result.BackupType);
    }

    [Fact]
    public async Task CreateBackupAsync_ShouldCreateMultipleBackupsForSameTenant()
    {
        // Arrange
        var tenantId = "test-tenant-multi";
        var ct = TestContext.Current.CancellationToken;

        // Act
        var backup1 = await _sut.CreateBackupAsync(tenantId, "Reason 1", ct);
        var backup2 = await _sut.CreateBackupAsync(tenantId, "Reason 2", ct);
        var backup3 = await _sut.CreateBackupAsync(tenantId, "Reason 3", ct);

        // Assert
        Assert.NotEqual(backup1.BackupId, backup2.BackupId);
        Assert.NotEqual(backup2.BackupId, backup3.BackupId);

        var backups = await _sut.ListBackupsAsync(tenantId, ct);
        Assert.Equal(3, backups.Count);
    }

    [Fact]
    public async Task ListBackupsAsync_ShouldReturnEmptyListForUnknownTenant()
    {
        // Arrange
        var tenantId = "unknown-tenant";
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.ListBackupsAsync(tenantId, ct);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ListBackupsAsync_ShouldReturnBackupsInReverseChronologicalOrder()
    {
        // Arrange
        var tenantId = "test-tenant-order";
        var ct = TestContext.Current.CancellationToken;
        await _sut.CreateBackupAsync(tenantId, "First", ct);
        await Task.Delay(10, ct); // Ensure different timestamps
        await _sut.CreateBackupAsync(tenantId, "Second", ct);
        await Task.Delay(10, ct);
        await _sut.CreateBackupAsync(tenantId, "Third", ct);

        // Act
        var result = await _sut.ListBackupsAsync(tenantId, ct);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("Third", result[0].Reason);
        Assert.Equal("Second", result[1].Reason);
        Assert.Equal("First", result[2].Reason);
    }

    [Fact]
    public async Task GetBackupAsync_ShouldReturnBackupById()
    {
        // Arrange
        var tenantId = "test-tenant-get";
        var ct = TestContext.Current.CancellationToken;
        var created = await _sut.CreateBackupAsync(tenantId, "Test backup", ct);

        // Act
        var result = await _sut.GetBackupAsync(tenantId, created.BackupId, ct);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(created.BackupId, result.BackupId);
        Assert.Equal(created.TenantId, result.TenantId);
        Assert.Equal(created.Reason, result.Reason);
    }

    [Fact]
    public async Task GetBackupAsync_ShouldReturnNullForUnknownBackupId()
    {
        // Arrange
        var tenantId = "test-tenant-unknown-backup";
        var ct = TestContext.Current.CancellationToken;
        await _sut.CreateBackupAsync(tenantId, "Test backup", ct);

        // Act
        var result = await _sut.GetBackupAsync(tenantId, "unknown-backup-id", ct);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetBackupAsync_ShouldReturnNullForUnknownTenant()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.GetBackupAsync("unknown-tenant", "some-backup-id", ct);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteBackupAsync_ShouldDeleteExistingBackup()
    {
        // Arrange
        var tenantId = "test-tenant-delete";
        var ct = TestContext.Current.CancellationToken;
        var backup = await _sut.CreateBackupAsync(tenantId, "To be deleted", ct);

        // Act
        var deleted = await _sut.DeleteBackupAsync(tenantId, backup.BackupId, ct);

        // Assert
        Assert.True(deleted);

        var retrieved = await _sut.GetBackupAsync(tenantId, backup.BackupId, ct);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task DeleteBackupAsync_ShouldReturnFalseForUnknownBackupId()
    {
        // Arrange
        var tenantId = "test-tenant-delete-unknown";
        var ct = TestContext.Current.CancellationToken;
        await _sut.CreateBackupAsync(tenantId, "Some backup", ct);

        // Act
        var result = await _sut.DeleteBackupAsync(tenantId, "unknown-backup-id", ct);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteBackupAsync_ShouldReturnFalseForUnknownTenant()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.DeleteBackupAsync("unknown-tenant", "some-backup-id", ct);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RestoreBackupAsync_ShouldSucceedForExistingBackup()
    {
        // Arrange
        var tenantId = "test-tenant-restore";
        var ct = TestContext.Current.CancellationToken;
        var backup = await _sut.CreateBackupAsync(tenantId, "Test backup", ct);

        // Act
        var result = await _sut.RestoreBackupAsync(tenantId, backup.BackupId, ct);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.RestoredBackup);
        Assert.Equal(backup.BackupId, result.RestoredBackup.BackupId);
        Assert.Empty(result.Errors);
        Assert.Single(result.Warnings); // In-memory warning
    }

    [Fact]
    public async Task RestoreBackupAsync_ShouldFailForUnknownTenant()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.RestoreBackupAsync("unknown-tenant", "some-backup-id", ct);

        // Assert
        Assert.False(result.Success);
        Assert.Single(result.Errors);
        Assert.Contains("No backups found", result.Errors[0]);
    }

    [Fact]
    public async Task RestoreBackupAsync_ShouldFailForUnknownBackupId()
    {
        // Arrange
        var tenantId = "test-tenant-restore-unknown";
        var ct = TestContext.Current.CancellationToken;
        await _sut.CreateBackupAsync(tenantId, "Some backup", ct);

        // Act
        var result = await _sut.RestoreBackupAsync(tenantId, "unknown-backup-id", ct);

        // Assert
        Assert.False(result.Success);
        Assert.Single(result.Errors);
        Assert.Contains("not found", result.Errors[0]);
    }

    [Fact]
    public async Task BackupsForDifferentTenants_ShouldBeIsolated()
    {
        // Arrange
        var tenant1 = "tenant-isolated-1";
        var tenant2 = "tenant-isolated-2";
        var ct = TestContext.Current.CancellationToken;

        await _sut.CreateBackupAsync(tenant1, "Tenant 1 backup 1", ct);
        await _sut.CreateBackupAsync(tenant1, "Tenant 1 backup 2", ct);
        await _sut.CreateBackupAsync(tenant2, "Tenant 2 backup 1", ct);

        // Act
        var tenant1Backups = await _sut.ListBackupsAsync(tenant1, ct);
        var tenant2Backups = await _sut.ListBackupsAsync(tenant2, ct);

        // Assert
        Assert.Equal(2, tenant1Backups.Count);
        Assert.Single(tenant2Backups);
        Assert.All(tenant1Backups, b => Assert.Equal(tenant1, b.TenantId));
        Assert.All(tenant2Backups, b => Assert.Equal(tenant2, b.TenantId));
    }
}
