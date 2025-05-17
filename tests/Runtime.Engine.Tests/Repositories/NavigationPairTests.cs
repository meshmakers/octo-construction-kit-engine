using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Repositories;

public class NavigationPairTests
{
    [Fact]
    public void Merge_Same_OK()
    {
        var navigationPair1 = new NavigationPair("System/ParentChild", GraphDirections.Outbound, "Test/Zone",
            new List<NavigationPair>
            {
                new("System/ParentChild", GraphDirections.Outbound, "Test/District"),
                new("Test/Demo", GraphDirections.Outbound, "Test/Item")
            }
        );

        var navigationPair2 = new NavigationPair("System/ParentChild", GraphDirections.Outbound, "Test/Zone",
            new List<NavigationPair>
            {
                new("System/ParentChild", GraphDirections.Outbound, "Test/District"),
                new("Test/Demo", GraphDirections.Outbound, "Test/Item")
            }
        );

        navigationPair1.Merge(navigationPair2);

        Assert.Equal("System/ParentChild", navigationPair1.CkRoleId);
        Assert.Equal("Test/Zone", navigationPair1.TargetCkTypeId);
        Assert.Equal(GraphDirections.Outbound, navigationPair1.Direction);
        Assert.Equal(2, navigationPair1.InnerNavigationPairs.Count);
        Assert.Equal("System/ParentChild", navigationPair1.InnerNavigationPairs[0].CkRoleId);
        Assert.Equal("Test/District", navigationPair1.InnerNavigationPairs[0].TargetCkTypeId);
        Assert.Equal(GraphDirections.Outbound, navigationPair1.InnerNavigationPairs[0].Direction);
        Assert.Equal("Test/Demo", navigationPair1.InnerNavigationPairs[1].CkRoleId);
        Assert.Equal("Test/Item", navigationPair1.InnerNavigationPairs[1].TargetCkTypeId);
        Assert.Equal(GraphDirections.Outbound, navigationPair1.InnerNavigationPairs[1].Direction);
    }

    [Fact]
    public void Merge_Different_TargetTypes_OK()
    {
        var navigationPair1 = new NavigationPair("System/ParentChild", GraphDirections.Outbound, "Test/Zone",
            new List<NavigationPair>
            {
                new("System/ParentChild", GraphDirections.Outbound, "Test/District"),
                new("Test/Demo", GraphDirections.Outbound, "Test/Item")
            }
        );

        var navigationPair2 = new NavigationPair("System/ParentChild", GraphDirections.Outbound, "Test/Zone",
            new List<NavigationPair>
            {
                new("System/ParentChild", GraphDirections.Outbound, "Test/District"),
                new("Test/Demo", GraphDirections.Outbound, "Test/Item2")
            }
        );

        navigationPair1.Merge(navigationPair2);

        Assert.Equal("System/ParentChild", navigationPair1.CkRoleId);
        Assert.Equal("Test/Zone", navigationPair1.TargetCkTypeId);
        Assert.Equal(GraphDirections.Outbound, navigationPair1.Direction);
        Assert.Equal(3, navigationPair1.InnerNavigationPairs.Count);
        Assert.Equal("System/ParentChild", navigationPair1.InnerNavigationPairs[0].CkRoleId);
        Assert.Equal("Test/District", navigationPair1.InnerNavigationPairs[0].TargetCkTypeId);
        Assert.Equal(GraphDirections.Outbound, navigationPair1.InnerNavigationPairs[0].Direction);
        Assert.Equal("Test/Demo", navigationPair1.InnerNavigationPairs[1].CkRoleId);
        Assert.Equal("Test/Item", navigationPair1.InnerNavigationPairs[1].TargetCkTypeId);
        Assert.Equal(GraphDirections.Outbound, navigationPair1.InnerNavigationPairs[1].Direction);
        Assert.Equal("Test/Demo", navigationPair1.InnerNavigationPairs[2].CkRoleId);
        Assert.Equal("Test/Item2", navigationPair1.InnerNavigationPairs[2].TargetCkTypeId);
        Assert.Equal(GraphDirections.Outbound, navigationPair1.InnerNavigationPairs[2].Direction);
    }
}