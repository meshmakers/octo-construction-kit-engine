using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Local;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
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
        var rtSerializer = _fixture.GetRtRepositorySerializer();
        var bulkRtMutation = _fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService,
            new LocalRepositoryDataSource(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        await Assert.ThrowsAsync<RuntimeRepositoryException>(async () => await localDirectoryRepository.CreateTransientRtEntityAsync("Test/LocationWithSensor"));
    }
    
    [Fact]
    public async Task CreateTransientRtEntity_OK()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();
        var rtSerializer = _fixture.GetRtRepositorySerializer();
        var bulkRtMutation = _fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService,
            new LocalRepositoryDataSource(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        var entity = await localDirectoryRepository.CreateTransientRtEntityAsync<RtSensor>();
        
        Assert.True(entity.IsEnabled);
    }
    
    [Fact]
    public async Task InsertOneRtEntityAsync_Abstract_Exception()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();
        var rtSerializer = _fixture.GetRtRepositorySerializer();
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
    public async Task InsertOneRtEntityAsync_MandatoryAttributeMissing_Exception()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();
        var rtSerializer = _fixture.GetRtRepositorySerializer();
        var bulkRtMutation = _fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService,
            new LocalRepositoryDataSource(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        var rtEntity = await localDirectoryRepository.CreateTransientRtEntityAsync("Test/Sensor");
        rtEntity.SetAttributeValue(TestCkIds.DesignationAttribute, AttributeValueTypesDto.String, "TestSensor1");
        rtEntity.SetAttributeValue(TestCkIds.ConnectionStateAttribute, AttributeValueTypesDto.Enum, null);

        await Assert.ThrowsAsync<RuntimeRepositoryException>(async () =>
            await localDirectoryRepository.InsertOneRtEntityAsync(new LocalSession(), "Test/Sensor", rtEntity));
    }
    
    [Fact]
    public async Task InsertOneRtEntityAsync_OK()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();
        var rtSerializer = _fixture.GetRtRepositorySerializer();
        var bulkRtMutation = _fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService,
            new LocalRepositoryDataSource(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        var rtEntity = await localDirectoryRepository.CreateTransientRtEntityAsync("Test/Sensor");
        rtEntity.SetAttributeValue(TestCkIds.DesignationAttribute, AttributeValueTypesDto.String, "TestSensor1");
        rtEntity.SetAttributeValue(TestCkIds.ConnectionStateAttribute, AttributeValueTypesDto.Enum, 0);

        await localDirectoryRepository.InsertOneRtEntityAsync(new LocalSession(), "Test/Sensor", rtEntity);
    }
    
    [Fact]
    public async Task InsertOneRtEntityAsync_Typed_OK()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();
        var rtSerializer = _fixture.GetRtRepositorySerializer();
        var bulkRtMutation = _fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService,
            new LocalRepositoryDataSource(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        var rtEntity = await localDirectoryRepository.CreateTransientRtEntityAsync<RtSensor>();
        rtEntity.Designation = "TestSensor2";
        rtEntity.ConnectionState = RtConnectionStateEnum.NotConnected;

        await localDirectoryRepository.InsertOneRtEntityAsync(new LocalSession(), rtEntity);
    }
    
    [Fact]
    public async Task GetRtEntityByRtIdAsync_OK()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();
        var rtSerializer = _fixture.GetRtRepositorySerializer();
        var bulkRtMutation = _fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService,
            new LocalRepositoryDataSource(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        var rtEntity = await localDirectoryRepository.CreateTransientRtEntityAsync<RtSensor>();
        rtEntity.Designation = "TestSensor2";
        rtEntity.ConnectionState = RtConnectionStateEnum.NotConnected;

        await localDirectoryRepository.InsertOneRtEntityAsync(new LocalSession(), rtEntity);

        var copy = await localDirectoryRepository.GetRtEntityByRtIdAsync(new LocalSession(), new RtEntityId(rtEntity.CkTypeId, rtEntity.RtId));
        
        Assert.NotNull(copy);
        Assert.Equal(copy.RtId, rtEntity.RtId);
    }
    
    [Fact]
    public async Task ReplaceOneRtEntityByIdAsync_OK()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();
        var rtSerializer = _fixture.GetRtRepositorySerializer();
        var bulkRtMutation = _fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService,
            new LocalRepositoryDataSource(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        var rtEntity = await localDirectoryRepository.CreateTransientRtEntityAsync<RtSensor>();
        rtEntity.Designation = "TestSensor2";
        rtEntity.ConnectionState = RtConnectionStateEnum.NotConnected;
        rtEntity.DataCount = 5;
        rtEntity.LocationX = 43.4959;

        await localDirectoryRepository.InsertOneRtEntityAsync(new LocalSession(), rtEntity);
        
        var rtEntity2 = await localDirectoryRepository.CreateTransientRtEntityAsync<RtSensor>();
        rtEntity2.Designation = "TestSensor2";
        rtEntity2.ConnectionState = RtConnectionStateEnum.NotConnected;
        rtEntity2.DataCount = 6;
        rtEntity2.LocationX = 43.4959;

        await localDirectoryRepository.ReplaceOneRtEntityByIdAsync(new LocalSession(), rtEntity.RtId, rtEntity2);
        
        var copy = await localDirectoryRepository.GetRtEntityByRtIdAsync<RtSensor>(new LocalSession(), rtEntity.RtId);

        Assert.NotNull(copy);
        Assert.Equal(6, copy.DataCount);
        Assert.Equal(rtEntity.RtId, copy.RtId);
    }
    
    [Fact]
    public async Task UpdateOneRtEntityByIdAsync_OK()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();
        var rtSerializer = _fixture.GetRtRepositorySerializer();
        var bulkRtMutation = _fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService,
            new LocalRepositoryDataSource(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        var rtEntity = await localDirectoryRepository.CreateTransientRtEntityAsync<RtSensor>();
        rtEntity.Designation = "TestSensor2";
        rtEntity.ConnectionState = RtConnectionStateEnum.NotConnected;
        rtEntity.DataCount = 5;
        rtEntity.LocationX = 43.4959;

        await localDirectoryRepository.InsertOneRtEntityAsync(new LocalSession(), rtEntity);

        var rtEntity2 = new RtSensor
        {
            DataCount = 7
        };

        await localDirectoryRepository.UpdateOneRtEntityByIdAsync(new LocalSession(), rtEntity.RtId, rtEntity2);
        
        var copy = await localDirectoryRepository.GetRtEntityByRtIdAsync<RtSensor>(new LocalSession(), rtEntity.RtId);

        Assert.NotNull(copy);
        Assert.Equal(7, copy.DataCount);
        Assert.Equal(43.4959, copy.LocationX);
        Assert.Equal("TestSensor2", copy.Designation);
        Assert.Equal(rtEntity.RtId, copy.RtId);
    }
    
    [Fact]
    public async Task UpdateManyRtEntityAsync_OK()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();
        var rtSerializer = _fixture.GetRtRepositorySerializer();
        var bulkRtMutation = _fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService,
            new LocalRepositoryDataSource(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        for (int i = 0; i < 20; i++)
        {
            var rtSensor = await localDirectoryRepository.CreateTransientRtEntityAsync<RtSensor>();
            rtSensor.Designation = "TestSensor" + i;
            rtSensor.ConnectionState = RtConnectionStateEnum.NotConnected;
            rtSensor.DataCount = 5 + i;
            rtSensor.LocationX = 43.4959;
            
            await localDirectoryRepository.InsertOneRtEntityAsync(new LocalSession(), rtSensor);
        }
        
        var updateEntity = await localDirectoryRepository.CreateTransientRtEntityAsync<RtSensor>();
        updateEntity.DataCount = 15;

        await localDirectoryRepository.UpdateManyRtEntityAsync(new LocalSession(), new List<FieldFilter>
        {
            new(TestCkIds.DesignationAttribute, FieldFilterOperator.Equals, "TestSensor10")
        } , updateEntity);

        var dataQueryOperation = DataQueryOperation.Create()
            .FieldFilter(TestCkIds.DataCountAttribute, FieldFilterOperator.Equals, 15);
        
        var copy = await localDirectoryRepository.GetRtEntitiesByTypeAsync<RtSensor>(new LocalSession(), dataQueryOperation);

        Assert.Equal(1, copy.TotalCount);
        Assert.Single(copy.Items);
        Assert.Equal(15, copy.Items.ElementAt(0).DataCount);
    }


    [Fact]
    public async Task ApplyChanges_CreateAssociation_OK()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();
        var rtSerializer = _fixture.GetRtRepositorySerializer();
        var bulkRtMutation = _fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService,
            new LocalRepositoryDataSource(_fixture.TenantId, _fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        List<EntityUpdateInfo<RtEntity>> entityUpdateInfos = new();
        List<AssociationUpdateInfo> associationUpdateInfos = new();
        var rtZone = await localDirectoryRepository.CreateTransientRtEntityAsync<RtZone>();
        rtZone.Designation = "MyZone";
        entityUpdateInfos.Add(EntityUpdateInfo<RtEntity>.CreateInsert(rtZone));
        
        var rtSensor = await localDirectoryRepository.CreateTransientRtEntityAsync<RtSensor>();
        rtSensor.Designation = "TestSensor";
        rtSensor.ConnectionState = RtConnectionStateEnum.NotConnected;
        rtSensor.DataCount = 5;
        rtSensor.LocationX = 43.4959;
        entityUpdateInfos.Add(EntityUpdateInfo<RtEntity>.CreateInsert(rtSensor));

        associationUpdateInfos.Add(new AssociationUpdateInfo(rtSensor.ToRtEntityId(), rtZone.ToRtEntityId(), "System/ParentChild", AssociationModOptionsDto.Create));

        OperationResult operationResult = new();
        await localDirectoryRepository.ApplyChanges(new LocalSession(), entityUpdateInfos, associationUpdateInfos, operationResult);


        var rtAssociations = await localDirectoryRepository.GetRtAssociationsAsync(new LocalSession(), rtZone.RtId, GraphDirections.Inbound);

        var associations = rtAssociations.ToList();
        Assert.Single(associations);
        var assoc = associations.First();

        Assert.Equal(rtSensor.RtId, assoc.OriginRtId);
        Assert.Equal(rtSensor.CkTypeId, assoc.OriginCkTypeId);
        Assert.Equal(rtZone.RtId, assoc.TargetRtId);
        Assert.Equal(rtZone.CkTypeId, assoc.TargetCkTypeId);
        Assert.Equal("System/ParentChild", assoc.AssociationRoleId);
    }
}