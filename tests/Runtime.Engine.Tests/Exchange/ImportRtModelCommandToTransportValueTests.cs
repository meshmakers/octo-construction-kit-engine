using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Meshmakers.Octo.Runtime.Engine.Exchange;
using Meshmakers.Octo.Runtime.Engine.Tests.Fixtures;
using TestCkModel.Generated.Test.v1;

using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Exchange;

/// <summary>
/// Unit tests for <see cref="ImportRtModelCommand.ToTransportValue"/> — the repository-to-transport
/// conversion the runtime-state preserve pass applies before it writes an existing value onto the
/// import model (AB#4784).
///
/// The two shapes coincide for every scalar type, which is why the raw assignment this replaced
/// went unnoticed: it only broke for <c>Record</c> / <c>RecordArray</c>, where the repository holds
/// <see cref="RtRecord"/> and the transport container holds <see cref="RtRecordTcDto"/>. Handing
/// the former over made the import throw <c>InvalidCastException</c> further down, failing the
/// whole seed on any tenant that already had a value — in practice every tenant with Helm
/// <c>ValueOverride</c>s on a workload.
/// </summary>
public class ImportRtModelCommandToTransportValueTests(CacheServiceFixture fixture)
    : IClassFixture<CacheServiceFixture>
{
    [Fact]
    public async Task Record_ConvertsToTransportDtoCarryingRecordAndAttributeIds()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var record = new RtRecord(TestCkIds.RtCkTestRecordRecordId, new Dictionary<string, object?>
        {
            { "Designation", "DemoValueInnerString" },
        });

        var converted = ImportRtModelCommand.ToTransportValue(ckCacheService, fixture.TenantId, record);

        var dto = Assert.IsType<RtRecordTcDto>(converted);
        Assert.Equal(TestCkIds.RtCkTestRecordRecordId, dto.CkRecordId);
        var attribute = Assert.Single(dto.Attributes);
        Assert.Equal("Designation", attribute.Id.ElementId.Name);
        Assert.Equal("DemoValueInnerString", attribute.Value);
    }

    [Fact]
    public async Task RecordArray_ConvertsEveryElement()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var records = new List<RtRecord>
        {
            new(TestCkIds.RtCkTestRecordRecordId, new Dictionary<string, object?> { { "Designation", "first" } }),
            new(TestCkIds.RtCkTestRecordRecordId, new Dictionary<string, object?> { { "Designation", "second" } }),
        };

        var converted = ImportRtModelCommand.ToTransportValue(ckCacheService, fixture.TenantId, records);

        // Materialized, not a lazy projection: the value is written onto the import model and read
        // back later by AssignAttributes, so it must not depend on the source list still being alive.
        var list = Assert.IsType<List<object?>>(converted);
        Assert.Collection(list,
            item => Assert.Equal("first", Assert.IsType<RtRecordTcDto>(item).Attributes.Single().Value),
            item => Assert.Equal("second", Assert.IsType<RtRecordTcDto>(item).Attributes.Single().Value));
    }

    [Fact]
    public async Task RecordAttributeMissingOnTheInstance_IsOmittedRatherThanNulled()
    {
        // A record instance that never got a value for one of its attributes must not gain a null
        // entry on the way through — the import would then write that null over whatever the seed
        // declared.
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var record = new RtRecord(TestCkIds.RtCkTestRecordRecordId, new Dictionary<string, object?>());

        var converted = ImportRtModelCommand.ToTransportValue(ckCacheService, fixture.TenantId, record);

        Assert.Empty(Assert.IsType<RtRecordTcDto>(converted).Attributes);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(true)]
    [InlineData("adapter.example.com")]
    [InlineData(null)]
    public async Task ScalarValue_PassesThroughUnchanged(object? value)
    {
        // The overwhelming majority of runtime-state attributes are scalars; conversion must stay
        // a no-op for them or the preserve pass would start rewriting values it has no business
        // touching.
        var ckCacheService = await fixture.GetCacheServiceAsync();

        var converted = ImportRtModelCommand.ToTransportValue(ckCacheService, fixture.TenantId, value);

        Assert.Equal(value, converted);
    }

    [Fact]
    public async Task String_IsNotTreatedAsASequence()
    {
        // Guard for the IEnumerable branch: a string is IEnumerable<char>, not IEnumerable<object?>,
        // so it must fall through to the scalar path instead of being shredded into characters.
        var ckCacheService = await fixture.GetCacheServiceAsync();

        var converted = ImportRtModelCommand.ToTransportValue(ckCacheService, fixture.TenantId, "publicUri");

        Assert.Equal("publicUri", converted);
    }
}
