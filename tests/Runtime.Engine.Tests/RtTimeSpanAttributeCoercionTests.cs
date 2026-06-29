using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine.Tests;

/// <summary>
/// AB#4259: a TimeSpan attribute round-tripped through the ImportRt export/import JSON ends up
/// persisted as a bare-integer ticks string (e.g. <c>"9000000000"</c>) in the
/// <c>Dictionary&lt;string, object?&gt;</c> attribute store instead of the canonical BSON Int64.
/// Reading it back via the generated <c>GetAttributeValueOrDefault&lt;TimeSpan&gt;</c> accessor must
/// coerce that shape to ticks rather than throwing — before the fix it fell through to
/// <c>Convert.ChangeType(string, TimeSpan)</c> and threw <see cref="System.InvalidCastException"/>,
/// surfacing as the generic ASSET1002 "An error occurred" on <c>enableArchive</c>.
/// </summary>
public class RtTimeSpanAttributeCoercionTests
{
    [Fact]
    public void GetAttributeValueOrDefault_TimeSpan_BareTicksString_CoercedToTicks()
    {
        var entity = new RtEntity();
        // Simulate the corrupted import shape: raw string of ticks, not a TimeSpan / Int64.
        entity.SetAttributeRawValue("Period", "9000000000");

        var result = entity.GetAttributeValueOrDefault<TimeSpan>("Period");

        Assert.Equal(TimeSpan.FromMinutes(15), result);
    }

    [Fact]
    public void GetAttributeValueOrDefault_TimeSpan_Int64Ticks_CoercedToTicks()
    {
        var entity = new RtEntity();
        entity.SetAttributeRawValue("Period", 9000000000L);

        var result = entity.GetAttributeValueOrDefault<TimeSpan>("Period");

        Assert.Equal(TimeSpan.FromMinutes(15), result);
    }

    [Fact]
    public void GetAttributeValueOrDefault_TimeSpan_DotNetString_Coerced()
    {
        var entity = new RtEntity();
        entity.SetAttributeRawValue("Period", "00:15:00");

        var result = entity.GetAttributeValueOrDefault<TimeSpan>("Period");

        Assert.Equal(TimeSpan.FromMinutes(15), result);
    }

    [Fact]
    public void GetAttributeValueOrDefault_TimeSpan_Iso8601String_Coerced()
    {
        var entity = new RtEntity();
        entity.SetAttributeRawValue("Period", "PT15M");

        var result = entity.GetAttributeValueOrDefault<TimeSpan>("Period");

        Assert.Equal(TimeSpan.FromMinutes(15), result);
    }
}
