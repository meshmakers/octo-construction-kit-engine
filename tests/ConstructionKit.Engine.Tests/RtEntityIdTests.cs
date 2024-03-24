using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests;

public class RtEntityIdTests
{
    private static readonly RtEntityId RtEntityId = new(new CkId<CkTypeId>("Demo/Test"), new OctoObjectId("66004fda527ac79a03ecedd7"));

    [Fact]
    public void Serialize_Ok()
    {
        var r = JsonSerializer.Serialize(RtEntityId, new JsonSerializerOptions { WriteIndented = false });
        
        Assert.Equal(@"{""CkTypeId"":""Demo-1.0.0/Test-1.0.0"",""RtId"":""66004fda527ac79a03ecedd7""}", r);
    }
    
    [Fact]
    public void Deserialize_Ok()
    {
        var s = @"{""CkTypeId"":""Demo-1.0.0/Test-1.0.0"",""RtId"":""66004fda527ac79a03ecedd7""}";
        
        var rtEntityId = JsonSerializer.Deserialize<RtEntityId>(s);
        
        Assert.Equal(RtEntityId, rtEntityId);
    }
}