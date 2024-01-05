// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
///     Defines the inheritance of a construction kit record
/// </summary>
public class CkGraphRecordInheritance
{
    /// <summary>
    ///     Constructor
    /// </summary>
    /// <param name="inheritorCkRecordId">Origin construction kid record id</param>
    /// <param name="baseCkRecordId">Target construction kid record id</param>
    /// <param name="baseTypeDepthIndex">Number that describes the depth of the inheritance chain</param>
    public CkGraphRecordInheritance(CkId<CkRecordId> inheritorCkRecordId, CkId<CkRecordId> baseCkRecordId, int baseTypeDepthIndex)
    {
        InheritorCkRecordId = inheritorCkRecordId;
        BaseCkRecordId = baseCkRecordId;
        BaseTypeDepthIndex = baseTypeDepthIndex;
    }

    /// <summary>
    ///     Returns the construction kit record id of the origin record
    /// </summary>
    public CkId<CkRecordId> InheritorCkRecordId { get; set; }

    /// <summary>
    ///     Returns the construction kit record id of the target record
    /// </summary>
    public CkId<CkRecordId> BaseCkRecordId { get; set; }

    /// <summary>
    ///     Returns a number that describes the depth of the inheritance chain
    /// </summary>
    public int BaseTypeDepthIndex { get; set; }
}