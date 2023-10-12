using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Local;
using Meshmakers.Octo.Runtime.Engine.Repositories.Local;
using Meshmakers.Octo.Runtime.Engine.SystemTests.Fixtures;
using Meshmakers.Octo.Runtime.Engine.SystemTests.sampleData.CkTest.ConstructionKit.Generated.Test.v1;

namespace Meshmakers.Octo.Runtime.Engine.SystemTests.Repositories;

public class LocalDirectoryRepositoryTests : IClassFixture<CacheServiceFixture>
{
    private readonly CacheServiceFixture _fixture;
    
    public LocalDirectoryRepositoryTests(CacheServiceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateTransientRtEntity_Abstract_Exception()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();
        var rtSerializer = _fixture.GetRtSerializer();
        var bulkRtMutation = _fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService,
            new LocalRepositoryDataSource(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        Assert.Throws<RuntimeRepositoryException>(() => localDirectoryRepository.CreateTransientRtEntity("Test/LocationWithSensor"));
    }
    
    [Fact]
    public async Task InsertOneRtEntityAsync_Abstract_Exception()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();
        var rtSerializer = _fixture.GetRtSerializer();
        var bulkRtMutation = _fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService,
            new LocalRepositoryDataSource(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        var rtEntity = new RtEntity("Test/LocationWithSensor", OctoObjectId.GenerateNewId(), new Dictionary<string, object?>
        {
            { "Designation", "Test" }
        });

        await Assert.ThrowsAsync<RuntimeRepositoryException>(async () =>
            await localDirectoryRepository.InsertOneRtEntityAsync(new LocalSession(), "Test/LocationWithSensor", rtEntity));
    }
    
    [Fact]
    public async Task InsertOneRtEntityAsync_MandatoryAttributeMissing_OK()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();
        var rtSerializer = _fixture.GetRtSerializer();
        var bulkRtMutation = _fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService,
            new LocalRepositoryDataSource(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        var rtEntity = localDirectoryRepository.CreateTransientRtEntity("Test/Sensor");
        rtEntity.SetAttributeValue(TestCkIds.DesignationAttribute, AttributeValueTypesDto.String, "TestSensor1");
        rtEntity.SetAttributeValue(TestCkIds.ConnectionStateAttribute, AttributeValueTypesDto.Enum, null);

        await Assert.ThrowsAsync<RuntimeRepositoryException>(async () =>
            await localDirectoryRepository.InsertOneRtEntityAsync(new LocalSession(), "Test/Sensor", rtEntity));
    }
    
    [Fact]
    public async Task InsertOneRtEntityAsync_OK()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();
        var rtSerializer = _fixture.GetRtSerializer();
        var bulkRtMutation = _fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService,
            new LocalRepositoryDataSource(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        var rtEntity = localDirectoryRepository.CreateTransientRtEntity("Test/Sensor");
        rtEntity.SetAttributeValue(TestCkIds.DesignationAttribute, AttributeValueTypesDto.String, "TestSensor1");
        rtEntity.SetAttributeValue(TestCkIds.ConnectionStateAttribute, AttributeValueTypesDto.Enum, 0);

        await localDirectoryRepository.InsertOneRtEntityAsync(new LocalSession(), "Test/Sensor", rtEntity);
    }
    
    [Fact]
    public async Task InsertOneRtEntityAsync_Typed_OK()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();
        var rtSerializer = _fixture.GetRtSerializer();
        var bulkRtMutation = _fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService,
            new LocalRepositoryDataSource(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        var rtEntity = localDirectoryRepository.CreateTransientRtEntity<RtSensor>();
        rtEntity.Designation = "TestSensor2";
        rtEntity.ConnectionState = RtConnectionStateEnum.NotConnected;

        await localDirectoryRepository.InsertOneRtEntityAsync(new LocalSession(), rtEntity);
    }
    
    [Fact]
    public async Task GetRtEntityByRtIdAsync_OK()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();
        var rtSerializer = _fixture.GetRtSerializer();
        var bulkRtMutation = _fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService,
            new LocalRepositoryDataSource(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        var rtEntity = localDirectoryRepository.CreateTransientRtEntity<RtSensor>();
        rtEntity.Designation = "TestSensor2";
        rtEntity.ConnectionState = RtConnectionStateEnum.NotConnected;

        await localDirectoryRepository.InsertOneRtEntityAsync(new LocalSession(), rtEntity);

        var copy = await localDirectoryRepository.GetRtEntityByRtIdAsync(new LocalSession(), new RtEntityId(rtEntity.CkTypeId, rtEntity.RtId));
        
        Assert.NotNull(copy);
        Assert.Equal(copy.RtId, rtEntity.RtId);
    }
}