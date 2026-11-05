using System.Text;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Meshmakers.Octo.Runtime.Engine.Serialization;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Serialization;

/// <summary>
///     AB#8325 - the JSON entity stream hands out its entities in bulks of at most <c>maxCount</c>. Every entity is handed
///     out exactly once, in file order, and the event path does not depend on when a handler sets
///     <see cref="RtDeserializeEventArgs.IsHandled" />.
/// </summary>
public class RtDeserializeStreamBulkTests
{
    private static readonly string[] RtIds =
    [
        "6512b932eec8b8e3b83e8601", "6512b932eec8b8e3b83e8602", "6512b932eec8b8e3b83e8603",
        "6512b932eec8b8e3b83e8604", "6512b932eec8b8e3b83e8605"
    ];

    private static async Task<RtDeserializeStream> OpenAsync(int maxCount, params string[] rtIds)
    {
        var json = new StringBuilder();
        json.Append("{\"$schema\":\"https://schemas.meshmakers.cloud/runtime-model.schema.json\",");
        json.Append("\"dependencies\":[\"System\"],\"entities\":[");
        json.Append(string.Join(",", rtIds.Select(id =>
            $"{{\"rtId\":\"{id}\",\"ckTypeId\":\"Sample1/SampleType1\",\"attributes\":[]}}")));
        json.Append("]}");
        var stream = new RtDeserializeStream(new MemoryStream(Encoding.UTF8.GetBytes(json.ToString())), maxCount);
        await stream.InitializeAsync();
        return stream;
    }

    [Fact]
    public async Task Event_HandlerThatSetsIsHandledLate_GetsEveryEntityExactlyOnce()
    {
        using var stream = await OpenAsync(2, RtIds);
        var received = new List<string>();
        var release = new TaskCompletionSource();
        stream.BulkDeserialized += (_, args) =>
        {
            received.AddRange(args.DeserializedEntities.Select(e => e.RtId.ToString()));
            // The shape of the former async handler: IsHandled is set only after the handler returned to the stream.
            _ = release.Task.ContinueWith(_ => args.IsHandled = true, TaskScheduler.Default);
        };

        await stream.ReadAsync();
        release.SetResult();

        Assert.Equal(RtIds, received);
    }

    [Fact]
    public async Task ReadBulksAsync_HandsOutBulksOfMaxCount_InFileOrder_EachEntityOnce()
    {
        using var stream = await OpenAsync(2, RtIds);
        var bulks = new List<IReadOnlyList<RtEntityTcDto>>();

        await foreach (var bulk in stream.ReadBulksAsync(TestContext.Current.CancellationToken))
        {
            // A consumer that awaits its work per bulk, as the import does.
            await Task.Delay(10, TestContext.Current.CancellationToken);
            bulks.Add(bulk);
        }

        Assert.Equal([2, 2, 1], bulks.Select(b => b.Count));
        Assert.Equal(RtIds, bulks.SelectMany(b => b).Select(e => e.RtId.ToString()));
    }

    [Fact]
    public async Task ReadBulksAsync_ExceptionOfTheConsumer_StopsTheRead()
    {
        using var stream = await OpenAsync(2, RtIds);
        var seen = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in stream.ReadBulksAsync(TestContext.Current.CancellationToken))
            {
                seen++;
                await Task.Yield();
                throw new InvalidOperationException("write refused");
            }
        });

        Assert.Equal(1, seen);
    }

    [Fact]
    public async Task ReadBulksAsync_DuplicateRtIdInsideABulk_IsRefused()
    {
        using var stream = await OpenAsync(5, RtIds[0], RtIds[1], RtIds[0]);

        var ex = await Assert.ThrowsAsync<RuntimeModelParseException>(async () =>
        {
            await foreach (var _ in stream.ReadBulksAsync(TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Contains(RtIds[0], ex.Message);
    }

    [Fact]
    public async Task ReadBulksAsync_RecordValues_AreReadAsRecordTransportObjects()
    {
        const string json = """
            {"$schema":"https://schemas.meshmakers.cloud/runtime-model.schema.json","dependencies":["System"],"entities":[
              {"rtId":"6512b932eec8b8e3b83e8601","ckTypeId":"Sample1/SampleType1","attributes":[
                {"id":"Sample1/Name","value":"n"},
                {"id":"Sample1/Tags","value":["a","b"]},
                {"id":"Sample1/Single","value":{"ckRecordId":"Sample1/Item","attributes":[
                  {"id":"Sample1/Key","value":"k"},
                  {"id":"Sample1/Detail","value":{"ckRecordId":"Sample1/Detail","attributes":[{"id":"Sample1/Note","value":7}]}}]}},
                {"id":"Sample1/Items","value":[
                  {"ckRecordId":"Sample1/Item","attributes":[{"id":"Sample1/Key","value":"a"},{"id":"Sample1/Label","value":null}]},
                  {"ckRecordId":"Sample1/Item","attributes":[{"id":"Sample1/Key","value":"b"}]}]},
                {"id":"Sample1/Empty","value":[]}]}]}
            """;
        var stream = new RtDeserializeStream(new MemoryStream(Encoding.UTF8.GetBytes(json)), 10);
        await stream.InitializeAsync();

        var entity = Assert.Single(await SingleBulkAsync(stream));

        string[] names = ["Name", "Tags", "Single", "Items", "Empty"];
        object? Value(string name) => entity.Attributes[Array.IndexOf(names, name)].Value;
        Assert.Equal("n", Value("Name"));
        var single = Assert.IsType<RtRecordTcDto>(Value("Single"));
        Assert.StartsWith("Sample1/Item", single.CkRecordId.FullName);
        Assert.Equal("k", single.Attributes[0].Value);
        var detail = Assert.IsType<RtRecordTcDto>(single.Attributes[1].Value);
        Assert.Equal(7L, detail.Attributes[0].Value);
        var items = Assert.IsAssignableFrom<IEnumerable<object>>(Value("Items")).Cast<RtRecordTcDto>().ToList();
        Assert.Equal(["a", "b"], items.Select(i => i.Attributes[0].Value));
        Assert.Null(items[0].Attributes[1].Value);
        // Values that are not records stay as the reader produced them.
        Assert.IsNotType<RtRecordTcDto>(Value("Tags"));
        Assert.Empty(Assert.IsAssignableFrom<System.Collections.IEnumerable>(Value("Empty")).Cast<object>());
    }

    private static async Task<IReadOnlyList<RtEntityTcDto>> SingleBulkAsync(RtDeserializeStream stream)
    {
        var bulks = new List<IReadOnlyList<RtEntityTcDto>>();
        await foreach (var bulk in stream.ReadBulksAsync(TestContext.Current.CancellationToken))
        {
            bulks.Add(bulk);
        }

        return Assert.Single(bulks);
    }
}
