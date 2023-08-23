namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

public class CkGraphTypeInheritance 
{
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="inheritorCkTypeId">Origin construction kid type id</param>
    /// <param name="baseCkTypeId">Target construction kid type id</param>
    /// <param name="baseTypeDepthIndex">Number that describes the depth of the inheritance chain</param>
    public CkGraphTypeInheritance(CkId<CkTypeId> inheritorCkTypeId, CkId<CkTypeId> baseCkTypeId, int baseTypeDepthIndex)
    {
        InheritorCkTypeId = inheritorCkTypeId;
        BaseCkTypeId = baseCkTypeId;
        BaseTypeDepthIndex = baseTypeDepthIndex;
    }
    
    /// <summary>
    /// Returns the construction kit type id of the origin type
    /// </summary>
    public CkId<CkTypeId> InheritorCkTypeId { get; set; }
    
    /// <summary>
    /// Returns the construction kit type id of the target type
    /// </summary>
    public CkId<CkTypeId> BaseCkTypeId { get; set; }

    /// <summary>
    /// Returns a number that describes the depth of the inheritance chain
    /// </summary>
    public int BaseTypeDepthIndex { get; set; }
}