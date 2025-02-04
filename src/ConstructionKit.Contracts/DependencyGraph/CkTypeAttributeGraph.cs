using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
///     Represents a construction kit attribute of a type, record or association role in the dependency graph
/// </summary>
[DebuggerDisplay("Name = {" + nameof(AttributeName) + "}, CkAttributeId = {" + nameof(CkAttributeId) + "}")]
public class CkTypeAttributeGraph
{
    /// <summary>
    ///     Creates a new instance of <see cref="CkTypeAttributeGraph" />.
    /// </summary>
    /// <param name="ckAttributeId"></param>
    /// <param name="ckTypeAttributeDto"></param>
    /// <param name="ckAttributeGraph"></param>
    public CkTypeAttributeGraph(CkId<CkAttributeId> ckAttributeId, CkTypeAttributeDto ckTypeAttributeDto, CkAttributeGraph ckAttributeGraph)
    {
        CkAttributeId = ckAttributeId;
        AttributeName = ckTypeAttributeDto.AttributeName;
        AutoCompleteValues = ckTypeAttributeDto.AutoCompleteValues ?? ckTypeAttributeDto.AutoCompleteValues ?? new List<object>();
        AutoIncrementReference = ckTypeAttributeDto.AutoIncrementReference;
        ValueType = ckAttributeGraph.ValueType;
        ValueCkRecordId = ckAttributeGraph.ValueCkRecordId;
        ValueCkEnumId = ckAttributeGraph.ValueCkEnumId;
        DefaultValues = ckAttributeGraph.DefaultValues;
        IsOptional = ckTypeAttributeDto.IsOptional;
        IsDataStream = ckAttributeGraph.IsDataStream;
        Description = ckAttributeGraph.Description;
        MetaData = ckAttributeGraph.MetaData;
    }

    /// <summary>
    ///     Creates a new instance of <see cref="CkTypeAttributeGraph" />.
    /// </summary>
    /// <param name="ckAttributeId"></param>
    /// <param name="attributeName"></param>
    /// <param name="autoCompleteValues"></param>
    /// <param name="valueType"></param>
    /// <param name="valueCkRecordId"></param>
    /// <param name="valueCkEnumId"></param>
    /// <param name="autoIncrementReference"></param>
    /// <param name="metaData"></param>
    /// <param name="isDataStream"></param>
    /// <param name="defaultValues"></param>
    /// <param name="isOptional"></param>
    /// <param name="description"></param>
    [JsonConstructor]
    public CkTypeAttributeGraph(CkId<CkAttributeId> ckAttributeId, string attributeName, IReadOnlyCollection<object>? autoCompleteValues,
        AttributeValueTypesDto valueType, CkId<CkRecordId>? valueCkRecordId, CkId<CkEnumId>? valueCkEnumId,
        string? autoIncrementReference, ICollection<CkAttributeMetaDataDto>? metaData, bool isDataStream,
        ICollection<object>? defaultValues, bool isOptional, string? description)
    {
        CkAttributeId = ckAttributeId;
        AttributeName = attributeName;
        AutoCompleteValues = autoCompleteValues ?? new List<object>();
        AutoIncrementReference = autoIncrementReference;
        ValueType = valueType;
        ValueCkRecordId = valueCkRecordId;
        ValueCkEnumId = valueCkEnumId;
        DefaultValues = defaultValues;
        MetaData = metaData;
        IsDataStream = isDataStream;
        IsOptional = isOptional;
        Description = description;
    }

    /// <summary>
    ///     Gets or sets the CK attribute id.
    /// </summary>
    public CkId<CkAttributeId> CkAttributeId { get; }

    /// <summary>
    ///     Gets or sets the name of the attribute.
    /// </summary>
    public string AttributeName { get; }

    /// <summary>
    ///     Gets or sets a list of values that are used for auto completion.
    /// </summary>
    public IReadOnlyCollection<object> AutoCompleteValues { get; }

    /// <summary>
    ///     If auto completion is enabled, this property defines the attribute that is used as a reference for the auto completion values.
    /// </summary>
    public string? AutoIncrementReference { get; }

    /// <summary>
    ///     Value type of the attribute
    /// </summary>
    public AttributeValueTypesDto ValueType { get; }

    /// <summary>
    ///     Defines the record of the attribute if the value type is a model.
    /// </summary>
    public CkId<CkRecordId>? ValueCkRecordId { get; }

    /// <summary>
    ///     Defines the record of the attribute if the value type is a model.
    /// </summary>
    public CkId<CkEnumId>? ValueCkEnumId { get; }

    /// <summary>
    ///     Default value of the attribute
    /// </summary>
    public ICollection<object>? DefaultValues { get; }

    /// <summary>
    ///     If true, the attribute is optional, that means it can be null
    /// </summary>
    public bool IsOptional { get; }

    /// <summary>
    ///     Optional meta data of the attribute
    /// </summary>
    public ICollection<CkAttributeMetaDataDto>? MetaData { get; }

    /// <summary>
    ///     If true, the attribute is a data stream
    /// </summary>
    public bool IsDataStream { get; }

    /// <summary>
    ///     An optional description of the attribute
    /// </summary>
    public string? Description { get; }
}