using System.Text.Json;

using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.DependencyGraph;

public class CkTypeGraphTests
{
    private static readonly CkId<CkTypeId> SampleTypeId =
        new(new CkModelId("Sample", "1.0.0"), new CkTypeId("WatchTarget"));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new CkIdAttributeIdConverter(),
            new CkIdAssociationRoleIdConverter(),
            new CkIdTypeIdConverter(),
            new CkIdRecordIdConverter(),
            new CkIdEnumIdConverter(),
            new CkModelIdConverter(),
        }
    };

    [Fact]
    public void EnableChangeStreamPreAndPostImages_is_false_when_dto_flag_default()
    {
        var dto = new CkCompiledTypeDto
        {
            TypeId = new CkTypeId("WatchTarget"),
        };

        var graph = new CkTypeGraph(SampleTypeId, dto);

        Assert.False(graph.EnableChangeStreamPreAndPostImages);
    }

    [Fact]
    public void EnableChangeStreamPreAndPostImages_is_propagated_from_dto()
    {
        var dto = new CkCompiledTypeDto
        {
            TypeId = new CkTypeId("WatchTarget"),
            EnableChangeStreamPreAndPostImages = true,
        };

        var graph = new CkTypeGraph(SampleTypeId, dto);

        Assert.True(graph.EnableChangeStreamPreAndPostImages);
    }

    [Fact]
    public void EnableChangeStreamPreAndPostImages_round_trips_through_json_constructor()
    {
        var dto = new CkCompiledTypeDto
        {
            TypeId = new CkTypeId("WatchTarget"),
            EnableChangeStreamPreAndPostImages = true,
        };
        var original = new CkTypeGraph(SampleTypeId, dto);

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var restored = JsonSerializer.Deserialize<CkTypeGraph>(json, JsonOptions);

        Assert.NotNull(restored);
        Assert.True(restored!.EnableChangeStreamPreAndPostImages);
    }

    [Fact]
    public void EnableChangeStreamPreAndPostImages_round_trips_when_false()
    {
        var dto = new CkCompiledTypeDto
        {
            TypeId = new CkTypeId("WatchTarget"),
        };
        var original = new CkTypeGraph(SampleTypeId, dto);

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var restored = JsonSerializer.Deserialize<CkTypeGraph>(json, JsonOptions);

        Assert.NotNull(restored);
        Assert.False(restored!.EnableChangeStreamPreAndPostImages);
    }
}
