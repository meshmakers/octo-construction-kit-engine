using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
///     Represents an attribute in the dependency graph
/// </summary>
[DebuggerDisplay("{" + nameof(CkAttributeId) + "}")]
public class CkAttributeGraph
{
    /// <summary>
    ///     Creates a new instance of <see cref="CkAttributeGraph" />.
    /// </summary>
    /// <param name="ckAttributeId"></param>
    /// <param name="attributeDto"></param>
    public CkAttributeGraph(CkId<CkAttributeId> ckAttributeId, CkAttributeDto attributeDto)
    {
        CkAttributeId = ckAttributeId;
        ValueType = attributeDto.ValueType;
        ValueCkRecordId = attributeDto.ValueCkRecordId;
        ValueCkEnumId = attributeDto.ValueCkEnumId;
        DefaultValues = attributeDto.DefaultValues;
        IsRuntimeState = attributeDto.IsRuntimeState;
        Description = attributeDto.Description;
        MetaData = attributeDto.MetaData;
    }

    /// <summary>
    ///     Serialization constructor
    ///     Creates a new instance of <see cref="CkAttributeGraph" />.
    /// </summary>
    /// <param name="ckAttributeId"></param>
    /// <param name="valueType"></param>
    /// <param name="valueCkRecordId"></param>
    /// <param name="valueCkEnumId"></param>
    /// <param name="defaultValues"></param>
    /// <param name="isRuntimeState">When true, blueprint re-apply preserves the existing runtime value of this attribute</param>
    /// <param name="description">An optional description to the attribute</param>
    /// <param name="metaData">Optional meta data of the attribute</param>
    [JsonConstructor]
    public CkAttributeGraph(CkId<CkAttributeId> ckAttributeId, AttributeValueTypesDto valueType, CkId<CkRecordId>? valueCkRecordId,
        CkId<CkEnumId>? valueCkEnumId, ICollection<object>? defaultValues, bool isRuntimeState, string? description,
        ICollection<CkAttributeMetaDataDto>? metaData)
    {
        CkAttributeId = ckAttributeId;
        ValueType = valueType;
        ValueCkRecordId = valueCkRecordId;
        ValueCkEnumId = valueCkEnumId;
        DefaultValues = defaultValues;
        IsRuntimeState = isRuntimeState;
        Description = description;
        MetaData = metaData;
    }

    /// <summary>
    ///     Returns the ck attribute id of the attribute.
    /// </summary>
    public CkId<CkAttributeId> CkAttributeId { get; }

    /// <summary>
    ///     Returns the value type of the attribute.
    /// </summary>
    public AttributeValueTypesDto ValueType { get; }

    /// <summary>
    ///     Defines the record type of the attribute if the value type is a record.
    /// </summary>
    public CkId<CkRecordId>? ValueCkRecordId { get; set; }

    /// <summary>
    ///     Defines the enum type of the attribute if the value type is an enum.
    /// </summary>
    public CkId<CkEnumId>? ValueCkEnumId { get; set; }

    /// <summary>
    ///     Returns the default values of the attribute.
    /// </summary>
    public ICollection<object>? DefaultValues { get; }

    /// <summary>
    ///     When true, blueprint re-apply preserves the existing runtime value of this attribute
    ///     instead of overwriting it with the seed value. See <see cref="CkAttributeDto.IsRuntimeState"/>.
    /// </summary>
    public bool IsRuntimeState { get; }

    /// <summary>
    ///     An optional description of the attribute
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    ///     Optional meta data of the attribute
    /// </summary>
    public ICollection<CkAttributeMetaDataDto>? MetaData { get; }
}