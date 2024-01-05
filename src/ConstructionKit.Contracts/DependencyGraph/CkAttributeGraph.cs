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
        Description = attributeDto.Description;
        MetaData = attributeDto.MetaData;
        IsDataStream = attributeDto.IsDataStream ?? false;
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
    /// <param name="description">A optional description to the attribute</param>
    /// <param name="metaData">Optional meta data of the attribute</param>
    /// <param name="isDataStream">Optional flag that tells if an attribute is a data stream </param>
    [JsonConstructor]
    public CkAttributeGraph(CkId<CkAttributeId> ckAttributeId, AttributeValueTypesDto valueType, CkId<CkRecordId>? valueCkRecordId,
        CkId<CkEnumId>? valueCkEnumId, ICollection<object>? defaultValues, string? description,
        ICollection<CkAttributeMetaDataDto>? metaData, bool isDataStream)
    {
        CkAttributeId = ckAttributeId;
        ValueType = valueType;
        ValueCkRecordId = valueCkRecordId;
        ValueCkEnumId = valueCkEnumId;
        DefaultValues = defaultValues;
        Description = description;
        MetaData = metaData;
        IsDataStream = isDataStream;
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
    ///     A optional description of the attribute
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    ///     Optional meta data of the attribute
    /// </summary>
    public ICollection<CkAttributeMetaDataDto>? MetaData { get; }

    /// <summary>
    ///     Optional flag that tells if an attribute is a data stream
    /// </summary>
    public bool IsDataStream { get; }
}