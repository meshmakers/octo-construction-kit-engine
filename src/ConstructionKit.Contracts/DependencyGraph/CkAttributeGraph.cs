using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
/// Represents an attribute in the dependency graph
/// </summary>
[DebuggerDisplay("{" + nameof(CkAttributeId) + "}")]
public class CkAttributeGraph
{
    /// <summary>
    /// Creates a new instance of <see cref="CkAttributeGraph"/>.
    /// </summary>
    /// <param name="ckAttributeId"></param>
    /// <param name="attributeDto"></param>
    public CkAttributeGraph(CkId<CkAttributeId> ckAttributeId, CkAttributeDto attributeDto)
    {
        CkAttributeId = ckAttributeId;
        ValueType = attributeDto.ValueType;
        DefaultValues = attributeDto.DefaultValues;
        SelectionValues = attributeDto.SelectionValues;
    }
    
    /// <summary>
    /// Creates a new instance of <see cref="CkAttributeGraph"/>.
    /// </summary>
    /// <param name="ckAttributeId"></param>
    /// <param name="valueType"></param>
    /// <param name="defaultValues"></param>
    /// <param name="selectionValues"></param>
    [JsonConstructor]
    public CkAttributeGraph(CkId<CkAttributeId> ckAttributeId, AttributeValueTypesDto valueType, 
        ICollection<object>? defaultValues, ICollection<CkSelectionValueDto>? selectionValues)
    {
        CkAttributeId = ckAttributeId;
        ValueType = valueType;
        DefaultValues = defaultValues;
        SelectionValues = selectionValues;
    }
    
    /// <summary>
    /// Returns the ck attribute id of the attribute.
    /// </summary>
    public CkId<CkAttributeId> CkAttributeId { get; }

    /// <summary>
    /// Returns the value type of the attribute.
    /// </summary>
    public AttributeValueTypesDto ValueType { get; }

    /// <summary>
    /// Returns the default values of the attribute.
    /// </summary>
    public ICollection<object>? DefaultValues { get; }

    /// <summary>
    /// Returns the selection values of the attribute.
    /// </summary>
    public ICollection<CkSelectionValueDto>? SelectionValues { get; }
}