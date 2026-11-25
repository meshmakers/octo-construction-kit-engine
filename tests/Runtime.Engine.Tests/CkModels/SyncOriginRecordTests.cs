using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Models.System.Generated.System.v2;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;

namespace Meshmakers.Octo.Runtime.Engine.Tests.CkModels;

/// <summary>
///     AB#9770 (Feature AB#8673): System 2.10.0 adds the record <c>System/SyncOrigin</c> (source tenant, source RtId,
///     sync time) and the optional attribute <c>RtSyncOrigin</c> on <c>System/Entity</c>. Like the other
///     system-managed Entity attributes (<c>RtBlueprintSource</c>, ...) it lives in the attribute dictionary; the
///     generator emits the typed record class <see cref="RtSyncOriginRecord" />.
/// </summary>
public class SyncOriginRecordTests
{
    private static readonly RtCkId<CkTypeId> AnyEntityType = new("Test/T");

    private static RtEntity RoundTrip(RtEntity value)
    {
        var json = JsonSerializer.Serialize(value, RtSystemTextJsonSerializer.Default);
        var result = JsonSerializer.Deserialize<RtEntity>(json, RtSystemTextJsonSerializer.Default);
        Assert.NotNull(result);
        return result!;
    }

    [Fact]
    public void GeneratedRecord_CarriesTheSystemSyncOriginRecordId_AndTheThreeTypedAttributes()
    {
        var syncedAt = new DateTime(2026, 11, 25, 7, 30, 0, DateTimeKind.Utc);
        var record = new RtSyncOriginRecord
        {
            SourceTenantId = "parent",
            SourceRtId = "68259915225146801209c9f1",
            SyncedAt = syncedAt
        };

        Assert.StartsWith("System/SyncOrigin", record.CkRecordId.ToString(), StringComparison.Ordinal);
        Assert.Equal(SystemCkIds.RtCkSyncOriginRecordId.ToString(), record.CkRecordId.ToString());
        Assert.Equal("parent", record.Attributes["SourceTenantId"]);
        Assert.Equal("68259915225146801209c9f1", record.Attributes["SourceRtId"]);
        Assert.Equal(syncedAt, record.Attributes["SyncedAt"]);
    }

    [Fact]
    public void EntityWithSyncOrigin_SurvivesTheRoundTrip_AndReadsBackAsTypedRecord()
    {
        var sourceRtId = OctoObjectId.GenerateNewId().ToString();
        var syncedAt = new DateTime(2026, 11, 25, 7, 30, 0, DateTimeKind.Utc);
        var entity = new RtEntity(AnyEntityType, OctoObjectId.GenerateNewId(), new Dictionary<string, object?>
        {
            ["Name"] = "Meter 1",
            ["RtSyncOrigin"] = new RtSyncOriginRecord
            {
                SourceTenantId = "parent",
                SourceRtId = sourceRtId,
                SyncedAt = syncedAt
            }
        });

        var result = RoundTrip(entity);

        var raw = Assert.IsType<RtRecord>(result.Attributes["RtSyncOrigin"]);
        Assert.Equal(SystemCkIds.RtCkSyncOriginRecordId.ToString(), raw.CkRecordId.ToString());
        var origin = new RtSyncOriginRecord(raw);
        Assert.Equal("parent", origin.SourceTenantId);
        Assert.Equal(sourceRtId, origin.SourceRtId);
        Assert.Equal(syncedAt, origin.SyncedAt);
        Assert.Equal("Meter 1", (string?)result.Attributes["Name"]);
    }

    [Fact]
    public void EntityWithoutSyncOrigin_IsUnchanged_ByTheRoundTrip_AndGainsNoSyncOriginAttribute()
    {
        var entity = new RtEntity(AnyEntityType, OctoObjectId.GenerateNewId(),
            new Dictionary<string, object?> { ["Name"] = "Local meter" });

        var result = RoundTrip(entity);

        Assert.False(result.Attributes.ContainsKey("RtSyncOrigin"));
        Assert.Single(result.Attributes);
        Assert.Equal("Local meter", (string?)result.Attributes["Name"]);
        Assert.Equal(entity.RtId, result.RtId);
    }
}
