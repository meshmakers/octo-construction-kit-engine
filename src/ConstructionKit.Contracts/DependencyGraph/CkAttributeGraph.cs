using System.Diagnostics;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
/// Represents an attribute in the dependency graph
/// </summary>
[DebuggerDisplay("{" + nameof(CkAttributeId) + "}")]
public class CkAttributeGraph
{
    public CkAttributeGraph(CkId<CkAttributeId> ckAttributeId, CkAttributeDto attributeDto)
    {
        CkAttributeId = ckAttributeId;
        ValueType = attributeDto.ValueType;
        DefaultValues = attributeDto.DefaultValues;
        SelectionValues = attributeDto.SelectionValues;
    }
    
    public CkId<CkAttributeId> CkAttributeId { get; }

    public AttributeValueTypesDto ValueType { get; }

    public ICollection<object>? DefaultValues { get; }

    public ICollection<CkSelectionValueDto>? SelectionValues { get; }
}