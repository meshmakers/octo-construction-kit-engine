using System.Collections.Concurrent;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Meshmakers.Octo.Runtime.Engine.Serialization;
using Meshmakers.Octo.Runtime.Engine.SystemTests.Fixtures;
using RandomFriendlyNameGenerator;

namespace Meshmakers.Octo.Runtime.Engine.SystemTests.Serializers;

public class JsonSerializerTests : IClassFixture<TemporaryDirectoryFixture>
{
    private readonly TemporaryDirectoryFixture _fixture;
    private readonly ITestOutputHelper _testOutputHelper;

    public JsonSerializerTests(TemporaryDirectoryFixture fixture, ITestOutputHelper testOutputHelper)
    {
        _fixture = fixture;
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task BuildAndReadHugeFile_OK()
    {
        var rtJsonSerializer = new RtJsonSerializer();

        var tempDirectory = _fixture.CreateTempDirectory();
        var filePath = Path.Combine(tempDirectory, "rtModel2.json");

        await using var streamWriter = new StreamWriter(filePath);

        var modelRootDto = new RtModelRootTcDto();
        modelRootDto.Dependencies.Add("System");
        modelRootDto.Dependencies.Add("Sample1");

        var random = new Random();

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = 16
        };

        List<RtEntityTcDto> globalList = new();

        for (var j = 0; j < 200; j++)
        {
            var entities = new ConcurrentStack<RtEntityTcDto>();
            var testCount = 0;
            _testOutputHelper.WriteLine($"========= next {j}");

            Parallel.For(0, 10_000, options, i =>
            {
                var rtEntity = new RtEntityTcDto
                {
                    CkTypeId = "Sample1/SampleType3",
                    RtId = OctoObjectId.GenerateNewId()
                };
                rtEntity.Attributes.Add(new RtAttributeTcDto { Id = "System/Name", Value = NameGenerator.Identifiers.Get() });
                rtEntity.Attributes.Add(new RtAttributeTcDto { Id = "System/Enabled", Value = i % 2 == 0 });

                var entitiesCount = testCount;
                if (entitiesCount > 1)
                {
                    var next = random.Next(0, entitiesCount - 1);
                    var rtAssocEntity = entities.ElementAt(next);
                    rtEntity.Associations = new List<RtAssociationTcDto>
                        { new() { RoleId = "Sample1/Testing", TargetCkTypeId = rtAssocEntity.CkTypeId, TargetRtId = rtAssocEntity.RtId } };
                }

                entities.Push(rtEntity);
                Interlocked.Increment(ref testCount);

                if (entitiesCount % 1000 == 0)
                {
                    _testOutputHelper.WriteLine($"Generated: {entitiesCount}");
                }
            });

            globalList.AddRange(entities);
        }

        modelRootDto.Entities = globalList.ToList();

        _testOutputHelper.WriteLine("Serializing...");

        await rtJsonSerializer.SerializeAsync(streamWriter, modelRootDto);
        streamWriter.Close();

        // Read
        await using var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var list = new List<OctoObjectId>();

        var deserializeStream = await rtJsonSerializer.DeserializeStreamAsync(stream);

        deserializeStream.BulkDeserialized += (sender, args) =>
        {
            list.AddRange(args.DeserializedEntities.Select(x => x.RtId));
            args.IsHandled = true;
            _testOutputHelper.WriteLine($"BulkDeserialized entities {list.Count}");
        };

        await deserializeStream.ReadAsync();

        Assert.Equal(2, deserializeStream.Dependencies.Count);
        Assert.Equal(2_000_000, list.Count);
        Assert.Empty(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }
}