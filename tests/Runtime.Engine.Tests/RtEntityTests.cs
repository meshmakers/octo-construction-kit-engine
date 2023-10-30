using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine.Tests;

public class RtEntityTests
{

    [Fact]
    public void test()
    {
        RtEntity rtEntity = new RtEntity();
        rtEntity.SetAttributeValueNonNullable("test", AttributeValueTypesDto.RecordArray, new [] { new RtRecord() });
        
        var test = rtEntity.GetAttributeValues<RtRecord>("test");
    }
}