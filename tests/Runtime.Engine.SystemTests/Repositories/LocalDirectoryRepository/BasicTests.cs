using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Local;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Engine.Repositories.Local;
using Meshmakers.Octo.Runtime.Engine.SystemTests.Fixtures;
using TestCkModel.Generated.Test.v1;

namespace Meshmakers.Octo.Runtime.Engine.SystemTests.Repositories.LocalDirectoryRepository;

public class BasicTests(CacheServiceFixture fixture) : IClassFixture<CacheServiceFixture>
{
    [Fact]
    public async Task CreateTransientRtEntity_Abstract_Exception()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var rtSerializer = fixture.GetRtRepositorySerializer();
        var bulkRtMutation = fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(fixture.TenantId, fixture.RepositoryPath,
            ckCacheService,
            new LocalRepositoryDataSource(fixture.TenantId, fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        await Assert.ThrowsAsync<RuntimeRepositoryException>(async () =>
            await localDirectoryRepository.CreateTransientRtEntityAsync("Test/LocationWithSensor"));
    }

    [Fact]
    public async Task CreateTransientRtEntity_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var rtSerializer = fixture.GetRtRepositorySerializer();
        var bulkRtMutation = fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(fixture.TenantId, fixture.RepositoryPath,
            ckCacheService,
            new LocalRepositoryDataSource(fixture.TenantId, fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        var entity = await localDirectoryRepository.CreateTransientRtEntityAsync<RtSensor>();

        Assert.True(entity.IsEnabled);
    }

    [Fact]
    public async Task InsertOneRtEntityAsync_Abstract_Exception()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var rtSerializer = fixture.GetRtRepositorySerializer();
        var bulkRtMutation = fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(fixture.TenantId, fixture.RepositoryPath,
            ckCacheService,
            new LocalRepositoryDataSource(fixture.TenantId, fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        var rtEntity = new RtEntity("Test/LocationWithSensor", OctoObjectId.GenerateNewId(),
            new Dictionary<string, object?>
            {
                { "Designation", "Test" }
            });

        await Assert.ThrowsAsync<RuntimeRepositoryException>(async () =>
            await localDirectoryRepository.InsertOneRtEntityAsync(new LocalSession(), "Test/LocationWithSensor",
                rtEntity));
    }

    [Fact]
    public async Task InsertOneRtEntityAsync_MandatoryAttributeMissing_Exception()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var rtSerializer = fixture.GetRtRepositorySerializer();
        var bulkRtMutation = fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(fixture.TenantId, fixture.RepositoryPath,
            ckCacheService,
            new LocalRepositoryDataSource(fixture.TenantId, fixture.RepositoryPath, ckCacheService, rtSerializer),
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
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var rtSerializer = fixture.GetRtRepositorySerializer();
        var bulkRtMutation = fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(fixture.TenantId, fixture.RepositoryPath,
            ckCacheService,
            new LocalRepositoryDataSource(fixture.TenantId, fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        var rtEntity = await localDirectoryRepository.CreateTransientRtEntityAsync("Test/Ocean");
        rtEntity.SetAttributeValue(TestCkIds.DesignationAttribute, AttributeValueTypesDto.String, "TestSensor1");

        await localDirectoryRepository.InsertOneRtEntityAsync(new LocalSession(), "Test/Ocean", rtEntity);
    }

    [Fact]
    public async Task InsertOneRtEntityAsync_Typed_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var rtSerializer = fixture.GetRtRepositorySerializer();
        var bulkRtMutation = fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(fixture.TenantId, fixture.RepositoryPath,
            ckCacheService,
            new LocalRepositoryDataSource(fixture.TenantId, fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        var rtOcean = await localDirectoryRepository.CreateTransientRtEntityAsync<RtOcean>();
        rtOcean.Designation = "TestSensor2";

        await localDirectoryRepository.InsertOneRtEntityAsync(new LocalSession(), rtOcean);
    }

    [Fact]
    public async Task GetRtEntityByRtIdAsync_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var rtSerializer = fixture.GetRtRepositorySerializer();
        var bulkRtMutation = fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(fixture.TenantId, fixture.RepositoryPath,
            ckCacheService,
            new LocalRepositoryDataSource(fixture.TenantId, fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        var rtOcean = await localDirectoryRepository.CreateTransientRtEntityAsync<RtOcean>();
        rtOcean.Designation = "TestSensor2";

        await localDirectoryRepository.InsertOneRtEntityAsync(new LocalSession(), rtOcean);

        var copy = await localDirectoryRepository.GetRtEntityByRtIdAsync(new LocalSession(),
            new RtEntityId(rtOcean.CkTypeId ?? throw new Exception(), rtOcean.RtId));

        Assert.NotNull(copy);
        Assert.Equal(copy.RtId, rtOcean.RtId);
    }

    [Fact]
    public async Task ReplaceOneRtEntityByIdAsync_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var rtSerializer = fixture.GetRtRepositorySerializer();
        var bulkRtMutation = fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(fixture.TenantId, fixture.RepositoryPath,
            ckCacheService,
            new LocalRepositoryDataSource(fixture.TenantId, fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        var rtInsertOcean = await localDirectoryRepository.CreateTransientRtEntityAsync<RtOcean>();
        rtInsertOcean.Designation = "TestSensor2";

        await localDirectoryRepository.InsertOneRtEntityAsync(new LocalSession(), rtInsertOcean);

        var rtOcean = await localDirectoryRepository.CreateTransientRtEntityAsync<RtOcean>();
        rtOcean.Designation = "TestSensor3";

        await localDirectoryRepository.ReplaceOneRtEntityByIdAsync(new LocalSession(), rtInsertOcean.RtId, rtOcean);

        var copy = await localDirectoryRepository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(),
            rtInsertOcean.RtId);

        Assert.NotNull(copy);
        Assert.Equal("TestSensor3", copy.Designation);
        Assert.Equal(rtInsertOcean.RtId, copy.RtId);
    }

    [Fact]
    public async Task UpdateOneRtEntityByIdAsync_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var rtSerializer = fixture.GetRtRepositorySerializer();
        var bulkRtMutation = fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(fixture.TenantId, fixture.RepositoryPath,
            ckCacheService,
            new LocalRepositoryDataSource(fixture.TenantId, fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        var rtEntity = await localDirectoryRepository.CreateTransientRtEntityAsync<RtOcean>();
        rtEntity.Designation = "TestSensor2";

        await localDirectoryRepository.InsertOneRtEntityAsync(new LocalSession(), rtEntity);

        var rtEntity2 = new RtOcean
        {
            Designation = "TestSensor284358ß2"
        };

        await localDirectoryRepository.UpdateOneRtEntityByIdAsync(new LocalSession(), rtEntity.RtId, rtEntity2);

        var copy = await localDirectoryRepository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), rtEntity.RtId);

        Assert.NotNull(copy);
        Assert.Equal("TestSensor284358ß2", copy.Designation);
        Assert.Equal(rtEntity.RtId, copy.RtId);
    }

    [Fact]
    public async Task UpdateManyRtEntityAsync_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var rtSerializer = fixture.GetRtRepositorySerializer();
        var bulkRtMutation = fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(fixture.TenantId, fixture.RepositoryPath,
            ckCacheService,
            new LocalRepositoryDataSource(fixture.TenantId, fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);

        for (var i = 0; i < 20; i++)
        {
            var rtOcean = await localDirectoryRepository.CreateTransientRtEntityAsync<RtOcean>();
            rtOcean.Designation = "TestSensor" + i;

            await localDirectoryRepository.InsertOneRtEntityAsync(new LocalSession(), rtOcean);
        }

        var updateEntity = await localDirectoryRepository.CreateTransientRtEntityAsync<RtOcean>();
        updateEntity.Designation = "TestSensor154737";

        await localDirectoryRepository.UpdateManyRtEntityAsync(new LocalSession(),
            FieldFilterCriteria.Create().FieldEquals(TestCkIds.DesignationAttribute, "TestSensor0"),
            updateEntity);

        var dataQueryOperation = DataQueryOperation.Create()
            .FieldFilter(TestCkIds.DesignationAttribute, FieldFilterOperator.Equals, "TestSensor154737");

        var copy = await localDirectoryRepository.GetRtEntitiesByTypeAsync<RtOcean>(new LocalSession(),
            dataQueryOperation);

        Assert.Equal(1, copy.TotalCount);
        Assert.Single(copy.Items);
        Assert.Equal("TestSensor154737", copy.Items.ElementAt(0).Designation);
    }


    [Fact]
    public async Task ApplyChanges_CreateAssociation_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var rtSerializer = fixture.GetRtRepositorySerializer();
        var bulkRtMutation = fixture.GetBulkRtMutation();
        var localDirectoryRepository = new LocalDirectoryRuntimeRepository(fixture.TenantId, fixture.RepositoryPath,
            ckCacheService,
            new LocalRepositoryDataSource(fixture.TenantId, fixture.RepositoryPath, ckCacheService, rtSerializer),
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

        associationUpdateInfos.Add(new AssociationUpdateInfo(rtSensor.ToRtEntityId(), rtZone.ToRtEntityId(),
            "System/ParentChild",
            AssociationModOptionsDto.Create));

        OperationResult operationResult = new();
        await localDirectoryRepository.ApplyChangesAsync(new LocalSession(), entityUpdateInfos, associationUpdateInfos,
            operationResult);


        var rtAssociations =
            await localDirectoryRepository.GetRtAssociationsAsync(new LocalSession(),
                rtZone.ToRtEntityId(),
                GraphDirections.Inbound);

        var associations = rtAssociations.Items.ToList();
        Assert.Single(associations);
        var assoc = associations.First();

        Assert.Equal(rtSensor.RtId, assoc.OriginRtId);
        Assert.Equal(rtSensor.CkTypeId, assoc.OriginCkTypeId);
        Assert.Equal(rtZone.RtId, assoc.TargetRtId);
        Assert.Equal(rtZone.CkTypeId, assoc.TargetCkTypeId);
        Assert.Equal("System/ParentChild", assoc.AssociationRoleId);
    }
}