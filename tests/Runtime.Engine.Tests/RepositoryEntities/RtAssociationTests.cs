using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.Runtime.Engine.Tests.RepositoryEntities;

public class RtAssociationTests
{
    [Fact]
    public void GetAttributeValue_TimeSpan_OK()
    {
        var rtAssociation = new RtAssociation();
        rtAssociation.SetAttributeValue("test", AttributeValueTypesDto.TimeSpan, "00:05:00");

        var test = rtAssociation.GetAttributeValue<TimeSpan>("test");

        Assert.Equal(TimeSpan.FromMinutes(5), test);
    }
}