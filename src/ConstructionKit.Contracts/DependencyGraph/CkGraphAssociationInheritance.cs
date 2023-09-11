using System.Text.Json.Serialization;
// ReSharper disable UnusedMember.Global

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
        Owned = new List<CkTypeAssociationGraph>();
        Inherited = new List<CkTypeAssociationGraph>();
    }
    
    /// <summary>
    /// Creates a new instance of <see cref="CkGraphAssociationInheritance"/>
    /// </summary>
    /// <param name="owned"></param>
    /// <param name="inherited"></param>
    [JsonConstructor]
    public CkGraphAssociationInheritance(ICollection<CkTypeAssociationGraph> owned, ICollection<CkTypeAssociationGraph> inherited)
    {
        Owned = owned;
        Inherited = inherited;
    }
    
    /// <summary>
    /// Returns the owned associations
    /// </summary>
    public ICollection<CkTypeAssociationGraph> Owned { get;  }
    
    /// <summary>
    /// Returns the inherited associations
    /// </summary>
    public ICollection<CkTypeAssociationGraph> Inherited { get;}
    
    /// <summary>
    /// Returns the sum of owned and inherited associations
    /// </summary>
    [JsonIgnore]
    public ICollection<CkTypeAssociationGraph> All => Owned.Concat(Inherited).ToList();
}