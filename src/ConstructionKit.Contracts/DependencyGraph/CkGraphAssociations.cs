using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

public class CkGraphAssociations
{
    public CkGraphAssociations()
    {
        Owned = new List<CkTypeAssociationDto>();
        Inherited = new List<CkTypeAssociationDto>();
    }
    
    public ICollection<CkTypeAssociationDto> Owned { get;  }
    public ICollection<CkTypeAssociationDto> Inherited { get;}
}