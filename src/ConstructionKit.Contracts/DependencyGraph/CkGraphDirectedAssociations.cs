using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
/// Represents the directed associations of a CK type
/// </summary>
public class CkGraphDirectedAssociations
{

    /// <summary>
    /// Creates a new instance of <see cref="CkGraphDirectedAssociations"/>
    /// </summary>
    public CkGraphDirectedAssociations(ICollection<CkTypeAssociationDto> definedAssociations)
    {
        DefinedAssociations = definedAssociations;
        In = new CkGraphAssociationInheritance();
        Out = new CkGraphAssociationInheritance();
    }

    /// <summary>
    /// Creates a new instance of <see cref="CkGraphDirectedAssociations"/>
    /// </summary>
    /// <param name="definedAssociations"></param>
    /// <param name="in"></param>
    /// <param name="out"></param>
    [JsonConstructor]
    public CkGraphDirectedAssociations(ICollection<CkTypeAssociationDto> definedAssociations , CkGraphAssociationInheritance @in, CkGraphAssociationInheritance @out)
    {
        DefinedAssociations = definedAssociations;
        In = @in;
        Out = @out;
    }
        
    /// <summary>
    /// Returns the defined associations of the type
    /// </summary>
    public ICollection<CkTypeAssociationDto> DefinedAssociations { get; }
    
    /// <summary>
    /// Returns the inbound associations
    /// </summary>
    public CkGraphAssociationInheritance In { get;  }
    
    /// <summary>
    /// Returns the outbound associations
    /// </summary>
    public CkGraphAssociationInheritance Out { get;  } 
    
}