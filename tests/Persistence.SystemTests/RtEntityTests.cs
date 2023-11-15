using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.SystematizedData.Persistence.SystemTests.CkTest.ConstructionKit.Generated.Test.v1;
using Meshmakers.Octo.SystematizedData.Persistence.SystemTests.Fixtures;
using Persistence.SystemCkModel.ConstructionKit.Generated.System.v1;
using Xunit;

namespace Meshmakers.Octo.SystematizedData.Persistence.SystemTests;

public class RtEntityTests : IClassFixture<SystemFixture>
{
    private readonly SystemFixture _systemFixture;

    public RtEntityTests(SystemFixture systemFixture)
    {
        _systemFixture = systemFixture;
    }

    [Fact]
    public async void TestGetIndirectRtAssociationTargets()
    {
        var systemContext = _systemFixture.GetSystemContext();
        var tenantRepository = await systemContext.GetTenantRepositoryAsync();

        using (var session = await tenantRepository.GetSessionAsync())
        {
            session.StartTransaction();

            OctoObjectId originRtId = OctoObjectId.Parse("5fc8fc3d8b2fc75f925e21bc");


            var r = await tenantRepository.GetIndirectRtAssociationTargetsAsync<RtCity, RtLocation>(session, originRtId,
                 SystemCkIds.ParentChild,
                 GraphDirections.Outbound);
        }
    }

    [Fact]
    public async void Test1()
    {
        var systemContext = _systemFixture.GetSystemContext();
        var tenantRepository = await systemContext.GetTenantRepositoryAsync();

        using (var session = await tenantRepository.GetSessionAsync())
        {
            // var result = await tenantContext.GetRtEntitiesByTypeAsync(session, "PaketService.Contact",
            //     new DataQueryOperation(), 0, 5);
            //
            // var rtIds = result.Result.Select(x => x.RtId).ToList();
            // var deep = await tenantContext.GetRtAssociationTargetsAsync(session, rtIds,
            //     "System/PaketService.Contact", "System/ParentChild", "PaketService.ParcelShipment", GraphDirections.Outbound, null,
            //     new DataQueryOperation(), 0, 5);


            //  Assert.Equal(5, deep.Count);
            // Assert.Collection(deep, 
            //     pair => Assert.Equal(5, pair.Value.Result.Count()),
            //     pair => Assert.Equal(5, pair.Value.Result.Count()),
            //     pair => Assert.Equal(5, pair.Value.Result.Count()),
            //     pair => Assert.Equal(5, pair.Value.Result.Count()),
            //     pair => Assert.Equal(5, pair.Value.Result.Count())
            //     );
        }
    }

    [Fact]
    public async void Test2()
    {
        var systemContext = _systemFixture.GetSystemContext();
        var tenantRepository = await systemContext.GetTenantRepositoryAsync();

        using (var session = await tenantRepository.GetSessionAsync())
        {
            // var result = await tenantRepository.GetRtEntitiesByTypeAsync(session, "PaketService.ParcelShipment",
            //     new DataQueryOperation(), 0, 10);


            var dataOperation = DataQueryOperation.Create()
                .FieldFilter("LastName", FieldFilterOperator.Like, "K*");
            // [LastName, Kastler]
            // dataOperation.SortOrders = new List<SortOrderItem>(new[]
            // {
            //     new SortOrderItem("LastName", SortOrders.Descending)
            // });

            // var rtIds = result.Result.Select(x => x.RtId).ToList();
            // var deep = await tenantRepository.GetRtAssociationTargetsAsync(session, rtIds,
            //     "PaketService.ParcelShipment", "System.ParentChild", "PaketService.Contact", GraphDirections.Inbound, null,
            //     dataOperation, 0, 5);


            // Assert.Collection(deep, 
            //     pair => Assert.Equal(5, pair.Value.Result.Count()),
            //     pair => Assert.Equal(5, pair.Value.Result.Count()),
            //     pair => Assert.Equal(5, pair.Value.Result.Count()),
            //     pair => Assert.Equal(5, pair.Value.Result.Count()),
            //     pair => Assert.Equal(5, pair.Value.Result.Count())
            //     );
        }
    }
}