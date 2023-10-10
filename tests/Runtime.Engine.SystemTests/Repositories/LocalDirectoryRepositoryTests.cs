using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Local;
using Meshmakers.Octo.Runtime.Engine.Repositories.Local;
using Meshmakers.Octo.Runtime.Engine.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.SystemTests.Fixtures;
using Meshmakers.Octo.Runtime.Engine.SystemTests.sampleData.CkTest.ConstructionKit.Generated.Test.v1;
using Xunit.Abstractions;

namespace Meshmakers.Octo.Runtime.Engine.SystemTests.Repositories;

public class LocalDirectoryRepositoryTests : IClassFixture<CacheServiceFixture>
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly CacheServiceFixture _fixture;
    
    public LocalDirectoryRepositoryTests(CacheServiceFixture fixture, ITestOutputHelper testOutputHelper)
    {
        _fixture = fixture;
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task CreateTransientRtEntity_Abstract_Exception()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();
        var rtSerializer = await _fixture.GetRtSerializerAsync();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService,
            new LocalRepositoryDataSource(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService, rtSerializer),
            new EntityRuleEngine(ckCacheService));

        Assert.Throws<RuntimeRepositoryException>(() => localDirectoryRepository.CreateTransientRtEntity("Test/LocationWithSensor"));
    }
    
    [Fact]
    public async Task InsertOneRtEntityAsync_Abstract_Exception()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();
        var rtSerializer = await _fixture.GetRtSerializerAsync();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService,
            new LocalRepositoryDataSource(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService, rtSerializer),
            new EntityRuleEngine(ckCacheService));

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
        var rtSerializer = await _fixture.GetRtSerializerAsync();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService,
            new LocalRepositoryDataSource(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService, rtSerializer),
            new EntityRuleEngine(ckCacheService));

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
        var rtSerializer = await _fixture.GetRtSerializerAsync();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService,
            new LocalRepositoryDataSource(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService, rtSerializer),
            new EntityRuleEngine(ckCacheService));

        var rtEntity = localDirectoryRepository.CreateTransientRtEntity("Test/Sensor");
        rtEntity.SetAttributeValue(TestCkIds.DesignationAttribute, AttributeValueTypesDto.String, "TestSensor1");
        rtEntity.SetAttributeValue(TestCkIds.ConnectionStateAttribute, AttributeValueTypesDto.Enum, 0);

        await localDirectoryRepository.InsertOneRtEntityAsync(new LocalSession(), "Test/Sensor", rtEntity);
    }
    
    [Fact]
    public async Task InsertOneRtEntityAsync_Typed_OK()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();
        var rtSerializer = await _fixture.GetRtSerializerAsync();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService,
            new LocalRepositoryDataSource(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService, rtSerializer),
            new EntityRuleEngine(ckCacheService));

        var rtEntity = localDirectoryRepository.CreateTransientRtEntity<RtSensor>();
        rtEntity.Designation = "TestSensor2";
        rtEntity.ConnectionState = RtConnectionStateEnum.NotConnected;

        await localDirectoryRepository.InsertOneRtEntityAsync(new LocalSession(), rtEntity);
    }
}