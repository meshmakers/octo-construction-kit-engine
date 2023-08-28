using System.Text.Json.Serialization;

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
    /// Creates a new instance of <see cref="CkGraphDirectedAssociations"/>
    /// </summary>
    /// <param name="in"></param>
    /// <param name="out"></param>
    [JsonConstructor]
    public CkGraphDirectedAssociations(CkGraphAssociations @in, CkGraphAssociations @out)
    {
        In = @in;
        Out = @out;
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