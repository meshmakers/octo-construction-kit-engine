using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests;

public class RtEntityIdTests
{
    private static readonly RtEntityId RtEntityId = new(new RtCkId<CkTypeId>("Demo/Test"), new OctoObjectId("66004fda527ac79a03ecedd7"));

    [Fact]
    public void Serialize_Ok()
    {
        var r = JsonSerializer.Serialize(RtEntityId, new JsonSerializerOptions { WriteIndented = false });
        
        Assert.Equal(@"{""CkTypeId"":""Demo/Test"",""RtId"":""66004fda527ac79a03ecedd7""}", r);
    }
    
    [Fact]
    public void Deserialize_Ok()
    {
        var s = @"{""CkTypeId"":""Demo/Test"",""RtId"":""66004fda527ac79a03ecedd7""}";
        
        var rtEntityId = JsonSerializer.Deserialize<RtEntityId>(s);
        
        Assert.Equal(RtEntityId, rtEntityId);
    }
    
    [Fact]
    public void Serialize_NewtonsoftJson_Ok()
    {
        var demo = new Demo
        {
            Id = new RtEntityId("Industry.Energy/EnergyMeter", new OctoObjectId("65dc6d24cc529cdc46c84fcc"))
        };
        var s = @"{""Id"":""Industry.Energy/EnergyMeter@65dc6d24cc529cdc46c84fcc""}";
        var r = Newtonsoft.Json.JsonConvert.SerializeObject(demo);
        
        Assert.NotNull(r);
        Assert.Equal(s, r);
    }
    
    [Fact]
    public void Deserialize_NewtonsoftJson_Ok()
    {
        var s = @"{""Id"":""Industry.Energy/EnergyMeter@65dc6d24cc529cdc46c84fcc""}";
        var demo = Newtonsoft.Json.JsonConvert.DeserializeObject<Demo>(s);

        Assert.NotNull(demo);
        Assert.Equal("Industry.Energy/EnergyMeter", demo.Id.CkTypeId);
        Assert.Equal("65dc6d24cc529cdc46c84fcc", demo.Id.RtId.ToString());
    }

    public class Demo
    {
        public RtEntityId Id { get; set; }
    }
}