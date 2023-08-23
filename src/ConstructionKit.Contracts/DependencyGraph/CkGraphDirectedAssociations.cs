namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

public class CkGraphDirectedAssociations
{
    public CkGraphDirectedAssociations()
    {
        In = new CkGraphAssociations();
        Out = new CkGraphAssociations();
    }
    public CkGraphAssociations In { get;  } 
    public CkGraphAssociations Out { get;  } 
}