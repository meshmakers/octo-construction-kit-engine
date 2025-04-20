using Meshmakers.Octo.Runtime.Engine.Repositories.Local;
using Meshmakers.Octo.Runtime.Engine.SystemTests.Fixtures;

namespace Meshmakers.Octo.Runtime.Engine.SystemTests.Repositories.LocalDirectoryRepository;

public class LocalDirectoryRepositoryTemporaryBinaryTests(CacheServiceFixture fixture) : IClassFixture<CacheServiceFixture>
{
    [Fact]
    public async Task Cache_UploadTemporaryLargeBinaryAsync()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var rtSerializer = fixture.GetRtRepositorySerializer();
        var bulkRtMutation = fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(fixture.TenantId, fixture.RepositoryPath,
            ckCacheService,
            new LocalRepositoryDataSource(fixture.TenantId, fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        var filePath = "sampleData/largeBinaries/Customers.xlsx";
        var stream = File.OpenRead(filePath);

        var session = await localDirectoryRepository.GetSessionAsync();
        session.StartTransaction();

        var id = await localDirectoryRepository.UploadTemporaryLargeBinaryAsync(session, "Customers.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", DateTime.UtcNow.AddHours(1), stream,
            CancellationToken.None);

        var r = await localDirectoryRepository.GetTemporaryLargeBinaryAsync(session, id, CancellationToken.None);

        Assert.NotNull(r);
        Assert.Equal("Customers.xlsx", r.Filename);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", r.ContentType);
        Assert.Equal(5401, r.Size);
    }

    [Fact]
    public async Task Cache_DeleteTemporaryLargeBinaryAsync()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var rtSerializer = fixture.GetRtRepositorySerializer();
        var bulkRtMutation = fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(fixture.TenantId, fixture.RepositoryPath,
            ckCacheService,
            new LocalRepositoryDataSource(fixture.TenantId, fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        var filePath = "sampleData/largeBinaries/Customers.xlsx";
        var stream = File.OpenRead(filePath);

        var session = await localDirectoryRepository.GetSessionAsync();
        session.StartTransaction();

        var id = await localDirectoryRepository.UploadTemporaryLargeBinaryAsync(session, "Customers.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", DateTime.UtcNow.AddHours(1), stream,
            CancellationToken.None);

        await localDirectoryRepository.DeleteTemporaryLargeBinaryAsync(session, id, CancellationToken.None);

        var r = await localDirectoryRepository.GetTemporaryLargeBinaryAsync(session, id, CancellationToken.None);

        Assert.Null(r);
    }
}