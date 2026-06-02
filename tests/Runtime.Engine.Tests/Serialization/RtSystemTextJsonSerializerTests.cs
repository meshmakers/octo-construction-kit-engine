using System.Text.Json;
using Meshmakers.Octo.Runtime.Contracts.Serialization;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Serialization;

/// <summary>
/// The canonical Rt System.Text.Json serializer must match the legacy
/// <c>RtNewtonsoftSerializer</c> on character escaping: non-ASCII (umlauts) and
/// HTML-sensitive chars are emitted literally, not as <c>\uXXXX</c> escapes.
/// <c>SystemTextJsonOptions.Default</c> (octo-sdk) and every wire node (webhooks, HTTP,
/// pipeline-data events) derive from this bundle, so escaping here silently changes the
/// wire bytes for any payload containing umlauts — routine on a German industrial platform.
/// </summary>
public class RtSystemTextJsonSerializerTests
{
    [Theory]
    [InlineData("Mühle & Co")]
    [InlineData("Größe")]
    [InlineData("Straße <x>")]
    public void Default_NonAscii_EmittedLiterally(string value)
    {
        var json = JsonSerializer.Serialize(
            new Dictionary<string, string> { ["name"] = value },
            RtSystemTextJsonSerializer.Default);

        Assert.Contains(value, json);
        Assert.DoesNotContain("\\u00", json);
    }

    private enum SampleDirection
    {
        Unknown = 0,
        Consumption = 1,
        Production = 2
    }

    private sealed record EnumWrapper
    {
        public SampleDirection Direction { get; init; }
    }

    /// <summary>
    /// Newtonsoft parity: a plain CLR enum must serialize as its underlying INTEGER (RtNewtonsoftSerializer
    /// has no StringEnumConverter), not its member name. The previous JsonStringEnumConverter emitted the
    /// name, which broke pipeline consumers that read the value as Int (DataMapping@1/If@1/Switch@1).
    /// </summary>
    [Fact]
    public void Default_ClrEnum_WritesInteger_NotName()
    {
        var json = JsonSerializer.Serialize(
            new EnumWrapper { Direction = SampleDirection.Production },
            RtSystemTextJsonSerializer.Default);

        Assert.Contains("\"Direction\":2", json);
        Assert.DoesNotContain("Production", json);
    }

    /// <summary>
    /// Newtonsoft read-tolerance parity: the reader must accept BOTH the integer and the member-name
    /// string form (Newtonsoft's reader is lenient), so externally-supplied name-form payloads still parse.
    /// </summary>
    [Theory]
    [InlineData("{\"Direction\":2}")]
    [InlineData("{\"Direction\":\"Production\"}")]
    [InlineData("{\"Direction\":\"production\"}")]
    public void Default_ClrEnum_ReadsIntegerAndName(string json)
    {
        var wrapper = JsonSerializer.Deserialize<EnumWrapper>(json, RtSystemTextJsonSerializer.Default);

        Assert.NotNull(wrapper);
        Assert.Equal(SampleDirection.Production, wrapper!.Direction);
    }
}
