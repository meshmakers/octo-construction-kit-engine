using System.Text.Json.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
/// Saves the inheritance information of associations 
/// </summary>
public class CkGraphAssociationInheritance
{
    /// <summary>
    /// Creates a new instance of <see cref="CkGraphAssociationInheritance"/>
    /// </summary>
    public CkGraphAssociationInheritance()
    {
        Owned = new List<CkAssociationGraph>();
        Inherited = new List<CkAssociationGraph>();
    }
    
    /// <summary>
    /// Creates a new instance of <see cref="CkGraphAssociationInheritance"/>
    /// </summary>
    /// <param name="owned"></param>
    /// <param name="inherited"></param>
    [JsonConstructor]
    public CkGraphAssociationInheritance(ICollection<CkAssociationGraph> owned, ICollection<CkAssociationGraph> inherited)
    {
        Owned = owned;
        Inherited = inherited;
    }
    
    /// <summary>
    /// Returns the owned associations
    /// </summary>
    public ICollection<CkAssociationGraph> Owned { get;  }
    
    /// <summary>
    /// Returns the inherited associations
    /// </summary>
    public ICollection<CkAssociationGraph> Inherited { get;}
}