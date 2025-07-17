using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Engine.Repositories.Local;
using Meshmakers.Octo.Runtime.Engine.SystemTests.Fixtures;
using TestCkModel.Generated.Test.v1;

namespace Meshmakers.Octo.Runtime.Engine.SystemTests.Repositories.LocalDirectoryRepository;

public class LinkedBinariesTests(CacheServiceFixture fixture) : IClassFixture<CacheServiceFixture>
{
    [Fact]
    public async Task FileSystem_InsertOneRtEntityAsync()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var rtSerializer = fixture.GetRtRepositorySerializer();
        var bulkRtMutation = fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(fixture.TenantId, fixture.RepositoryPath,
            ckCacheService,
            new LocalRepositoryDataSource(fixture.TenantId, fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        var (session, binaryEntity) = await InsertCustomers(localDirectoryRepository);

        await session.CommitTransactionAsync();

        var r = await GetRtBinaryEntity(localDirectoryRepository, binaryEntity);

        Assert.NotNull(r);

    }

    [Fact]
    public async Task FileSystem_DeleteOneRtEntityAsync()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var rtSerializer = fixture.GetRtRepositorySerializer();
        var bulkRtMutation = fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(fixture.TenantId, fixture.RepositoryPath,
            ckCacheService,
            new LocalRepositoryDataSource(fixture.TenantId, fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        var (session, binaryEntity) = await InsertCustomers(localDirectoryRepository);

        await localDirectoryRepository.DeleteOneRtEntityByRtIdAsync<RtBinaryEntity>(session, binaryEntity.RtId);

        await session.CommitTransactionAsync();
    }

    [Fact]
    public async Task FileSystem_ReplaceOneRtEntityByIdAsync()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var rtSerializer = fixture.GetRtRepositorySerializer();
        var bulkRtMutation = fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(fixture.TenantId, fixture.RepositoryPath,
            ckCacheService,
            new LocalRepositoryDataSource(fixture.TenantId, fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        var (session, binaryEntity) = await InsertCustomers(localDirectoryRepository);

        await session.CommitTransactionAsync();

        var filePath = "sampleData/largeBinaries/Products.pdf";
        var stream = File.OpenRead(filePath);

        var replaceSession = await localDirectoryRepository.GetSessionAsync();
        replaceSession.StartTransaction();

        var replaceBinaryEntity = new RtBinaryEntity
        {
            DataCount = 5,
            Binary = new EntityBinaryInfo
            {
                Filename = "Products.pdf",
                ContentType = "application/pdf",
                Stream = stream
            }
        };

        await localDirectoryRepository.ReplaceOneRtEntityByIdAsync(replaceSession, binaryEntity.RtId, replaceBinaryEntity);

        await replaceSession.CommitTransactionAsync();

        var r = await GetRtBinaryEntity(localDirectoryRepository, binaryEntity);

        Assert.NotNull(r);
        Assert.Equal(replaceBinaryEntity.RtId, r.RtId);
        Assert.Equal("Products.pdf", r.Binary.Filename);
        Assert.Equal("application/pdf", r.Binary.ContentType);
        Assert.Equal(56987, r.Binary.Size);
    }

    [Fact]
    public async Task FileSystem_UpdateOneRtEntityByIdAsync()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var rtSerializer = fixture.GetRtRepositorySerializer();
        var bulkRtMutation = fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(fixture.TenantId, fixture.RepositoryPath,
            ckCacheService,
            new LocalRepositoryDataSource(fixture.TenantId, fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        var (session, binaryEntity) = await InsertCustomers(localDirectoryRepository);

        await session.CommitTransactionAsync();

        var filePath = "sampleData/largeBinaries/Products.pdf";
        var stream = File.OpenRead(filePath);

        var replaceSession = await localDirectoryRepository.GetSessionAsync();
        replaceSession.StartTransaction();

        var replaceBinaryEntity = new RtBinaryEntity
        {
            Binary = new EntityBinaryInfo
            {
                Filename = "Products.pdf",
                ContentType = "application/pdf",
                Stream = stream
            }
        };

        await localDirectoryRepository.UpdateOneRtEntityByIdAsync(replaceSession, binaryEntity.RtId, replaceBinaryEntity);

        await replaceSession.CommitTransactionAsync();

        var r = await GetRtBinaryEntity(localDirectoryRepository, binaryEntity);

        Assert.NotNull(r);
        Assert.Equal(replaceBinaryEntity.RtId, r.RtId);
        Assert.Equal(replaceBinaryEntity.Binary.BinaryId, r.Binary.BinaryId);
        Assert.Equal("Products.pdf", r.Binary.Filename);
        Assert.Equal("application/pdf", r.Binary.ContentType);
        Assert.Equal(56987, r.Binary.Size);
    }


    private static async Task<(IOctoSession session, RtBinaryEntity binaryEntity)> InsertCustomers(LocalDirectoryRuntimeRepository localDirectoryRepository)
    {
        var filePath = "sampleData/largeBinaries/Customers.xlsx";
        var stream = File.OpenRead(filePath);

        var session = await localDirectoryRepository.GetSessionAsync();
        session.StartTransaction();

        RtBinaryEntity binaryEntity = new()
        {
            RtId = OctoObjectId.GenerateNewId(),
            DataCount = 5,
            Binary = new EntityBinaryInfo
            {
                Filename = "Customers.xlsx",
                ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                Stream = stream
            }
        };

        await localDirectoryRepository.InsertOneRtEntityAsync(session, binaryEntity);
        return (session, binaryEntity);
    }

    private static async Task<RtBinaryEntity?> GetRtBinaryEntity(LocalDirectoryRuntimeRepository localDirectoryRepository,
        RtBinaryEntity binaryEntity)
    {
        var sessionRead = await localDirectoryRepository.GetSessionAsync();
        sessionRead.StartTransaction();

        var r = await localDirectoryRepository.GetRtEntityByRtIdAsync<RtBinaryEntity>(sessionRead, binaryEntity.RtId);

        await sessionRead.CommitTransactionAsync();
        return r;
    }
}