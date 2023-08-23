namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
/// Represents the directed associations of a CK type
/// </summary>
public class CkGraphDirectedAssociations
{
    /// <summary>
    /// Creates a new instance of <see cref="CkGraphDirectedAssociations"/>
    /// </summary>
    public CkGraphDirectedAssociations()
    {
        In = new CkGraphAssociations();
        Out = new CkGraphAssociations();
    }
    
    /// <summary>
    /// Returns the inbound associations
    /// </summary>
    public CkGraphAssociations In { get;  }
    
    /// <summary>
    /// Returns the outbound associations
    /// </summary>
    public CkGraphAssociations Out { get;  } 
}