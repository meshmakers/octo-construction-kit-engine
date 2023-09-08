using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;

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
        ValueCkRecordId = attributeDto.ValueCkRecordId;
        DefaultValues = attributeDto.DefaultValues;
        IsOptional = attributeDto.IsOptional;
        Description = attributeDto.Description;
    }

    /// <summary>
    /// Creates a new instance of <see cref="CkAttributeGraph"/>.
    /// </summary>
    /// <param name="ckAttributeId"></param>
    /// <param name="valueType"></param>
    /// <param name="valueCkRecordId"></param>
    /// <param name="defaultValues"></param>
    /// <param name="isOptional">When true, the attribute value is optional, that means the value can be null.</param>
    /// <param name="description">A optional description to the attribute</param>
    [JsonConstructor]
    public CkAttributeGraph(CkId<CkAttributeId> ckAttributeId, AttributeValueTypesDto valueType, CkId<CkRecordId>? valueCkRecordId,
        ICollection<object>? defaultValues, bool isOptional, string? description)
    {
        CkAttributeId = ckAttributeId;
        ValueType = valueType;
        ValueCkRecordId = valueCkRecordId;
        DefaultValues = defaultValues;
        IsOptional = isOptional;
        Description = description;
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
    /// Defines the record type of the attribute if the value type is a record.
    /// </summary>
    public CkId<CkRecordId>? ValueCkRecordId { get; set; }

    /// <summary>
    /// Returns the default values of the attribute.
    /// </summary>
    public ICollection<object>? DefaultValues { get; }
    
    /// <summary>
    /// If true, the attribute is optional, that means it can be null
    /// </summary>
    public bool IsOptional { get; }
    
    /// <summary>
    /// A optional description of the attribute
    /// </summary>
    public string? Description { get; set; }
}