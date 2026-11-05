using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

/// <summary>
/// AB#8332: the import writes the columns of an archive in the order the tenant holds them; a seed that lists the same
/// columns in another order (the archive lifecycle appends the columns a seed adds) writes no change.
/// </summary>
public class ArchiveDefinitionGuardColumnOrderTests
{
    private const string ColumnRecord = "System.StreamData-1.22.0/CkArchiveColumn";

    [Fact]
    public void SeedColumns_TakeTheStoredOrder_NewColumnsFollowInSeedOrder()
    {
        var stored = new[]
        {
            Column(path: "StateOfCharge"), Column(path: "ChargingPower"), Column(path: "DischargingPower"),
            Column(name: "ChargePower"), Column(name: "DischargePower")
        };
        var seed = new List<object?>
        {
            Column(path: "StateOfCharge"), Column(path: "ChargingPower"), Column(name: "ChargePower"),
            Column(name: "NewB"), Column(path: "DischargingPower"), Column(name: "DischargePower"), Column(name: "NewA")
        };

        var ordered = ArchiveDefinitionGuard.OrderColumnsLike(stored, seed);

        Assert.Equal(
            ["stateofcharge", "chargingpower", "dischargingpower", "ChargePower", "DischargePower", "NewB", "NewA"],
            ordered.Cast<RtRecordTcDto>().Select(ArchiveDefinitionGuard.ColumnKeyOf));
        Assert.Equal(seed.Count, ordered.Count);
    }

    [Fact]
    public void SeedInTheStoredOrder_IsReturnedUnchanged()
    {
        var stored = new[] { Column(path: "a"), Column(name: "b") };
        var seed = new List<object?> { Column(path: "a"), Column(name: "b") };

        Assert.Equal(seed, ArchiveDefinitionGuard.OrderColumnsLike(stored, seed));
    }

    private static RtRecordTcDto Column(string? path = null, string? name = null)
    {
        var record = new RtRecordTcDto { CkRecordId = new CkId<CkRecordId>(ColumnRecord).ToRtCkId() };
        record.Attributes.Add(new RtAttributeTcDto
        {
            Id = new CkId<CkAttributeId>($"{ColumnRecord}.Path").ToRtCkId(), Value = path ?? string.Empty
        });
        if (name != null)
        {
            record.Attributes.Add(new RtAttributeTcDto
            {
                Id = new CkId<CkAttributeId>($"{ColumnRecord}.Name").ToRtCkId(), Value = name
            });
        }

        return record;
    }
}
