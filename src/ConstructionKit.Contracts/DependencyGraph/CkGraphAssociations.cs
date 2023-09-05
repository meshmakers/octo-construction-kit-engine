using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;

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
    /// Creates a new instance of <see cref="CkGraphAssociations"/>
    /// </summary>
    /// <param name="owned"></param>
    /// <param name="inherited"></param>
    [JsonConstructor]
    public CkGraphAssociations(ICollection<CkTypeAssociationDto> owned, ICollection<CkTypeAssociationDto> inherited)
    {
        Owned = owned;
        Inherited = inherited;
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