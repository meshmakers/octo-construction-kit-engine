using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

public class ArchiveRowOriginFieldsTests
{
    [Theory]
    [InlineData("manual_count", true)]
    [InlineData("last_manual_at", true)]
    [InlineData("MANUAL_COUNT", true)]
    [InlineData("Last_Manual_At", true)]
    [InlineData("manual_written_at", false)]
    [InlineData("manualcount", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsRollupField(string? columnName, bool expected)
    {
        Assert.Equal(expected, ArchiveRowOriginFields.IsRollupField(columnName));
    }
}
