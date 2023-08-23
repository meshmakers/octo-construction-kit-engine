using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
/// Represents the associations of a CK type
/// </summary>
public class CkGraphAssociations
{
    /// <summary>
    /// Creates a new instance of <see cref="CkGraphAssociations"/>
    /// </summary>
    public CkGraphAssociations()
    {
        Owned = new List<CkTypeAssociationDto>();
        Inherited = new List<CkTypeAssociationDto>();
    }
    
    /// <summary>
    /// Returns the owned associations
    /// </summary>
    public ICollection<CkTypeAssociationDto> Owned { get;  }
    
    /// <summary>
    /// Returns the inherited associations
    /// </summary>
    public ICollection<CkTypeAssociationDto> Inherited { get;}
}