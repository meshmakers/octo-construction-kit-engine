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
        AutoCompleteValues = ckTypeAttributeDto.AutoCompleteValues ?? ckTypeAttributeDto.AutoCompleteValues ?? [];
        AutoIncrementReference = ckTypeAttributeDto.AutoIncrementReference;
        ValueType = ckAttributeGraph.ValueType;
        ValueCkRecordId = ckAttributeGraph.ValueCkRecordId;
        ValueCkEnumId = ckAttributeGraph.ValueCkEnumId;
        DefaultValues = ckAttributeGraph.DefaultValues;
        // AB#5187: the assignment's override wins, the definition is the default.
        Ownership = AttributeOwnership.Resolve(ckTypeAttributeDto.Ownership, ckAttributeGraph.Ownership);
        IsOptional = ckTypeAttributeDto.IsOptional;
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
    /// <param name="defaultValues"></param>
    /// <param name="isOptional"></param>
    /// <param name="description"></param>
    /// <param name="isRuntimeState">
    /// DEPRECATED alias for <see cref="Ownership"/>, kept so CK caches and callers that pre-date
    /// AB#5187 keep working: it seeds <see cref="Ownership"/>, and a deserialized <c>ownership</c>
    /// key, when present, then overwrites it.
    /// Trailing + defaulted so existing positional callers (e.g. the mesh-adapter SDK tests
    /// that build a <see cref="CkTypeAttributeGraph"/> directly) compile unchanged. STJ binds
    /// the constructor by parameter name, so the JSON wire format is unaffected.
    /// </param>
    [JsonConstructor]
    public CkTypeAttributeGraph(CkId<CkAttributeId> ckAttributeId, string attributeName, IReadOnlyCollection<object>? autoCompleteValues,
        AttributeValueTypesDto valueType, CkId<CkRecordId>? valueCkRecordId, CkId<CkEnumId>? valueCkEnumId,
        string? autoIncrementReference, ICollection<CkAttributeMetaDataDto>? metaData,
        ICollection<object>? defaultValues, bool isOptional, string? description, bool isRuntimeState = false)
    {
        CkAttributeId = ckAttributeId;
        AttributeName = attributeName;
        AutoCompleteValues = autoCompleteValues ?? new List<object>();
        AutoIncrementReference = autoIncrementReference;
        ValueType = valueType;
        ValueCkRecordId = valueCkRecordId;
        ValueCkEnumId = valueCkEnumId;
        DefaultValues = defaultValues;
        // `ownership` is not a constructor parameter: STJ requires a parameter's type to
        // match its property's, and a nullable parameter cannot bind to the non-nullable resolved
        // property. It is deserialized through the property setter instead, which STJ applies
        // AFTER the constructor — so a cache written by an older engine (no `ownership` key) keeps
        // the value resolved here from the deprecated alias, and a current cache overwrites it
        // with the exact declared value.
        Ownership = AttributeOwnership.Resolve(null, isRuntimeState);
        MetaData = metaData;
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
    ///     EFFECTIVE ownership of this attribute assignment (AB#5187): the assignment's
    ///     <see cref="CkTypeAttributeDto.Ownership"/> override when declared, otherwise the
    ///     attribute definition's <see cref="CkAttributeGraph.Ownership"/>. This is the value both
    ///     consumers read — upsert preservation and <c>ExportRt</c> exclusion — each through its
    ///     own predicate.
    /// </summary>
    public AttributeOwnershipDto Ownership { get; set; }

    /// <summary>
    ///     DEPRECATED mirror of <see cref="Ownership"/> meaning "preserved on Upsert". Kept for
    ///     the JSON cache wire format and for consumers that pre-date AB#5187. NOTE it is NOT the
    ///     export predicate any more: a <see cref="AttributeOwnershipDto.TenantOwned"/> attribute
    ///     mirrors as <c>true</c> here and is still exported. Use
    ///     <see cref="AttributeOwnership.IsExcludedFromExport"/> for that question.
    /// </summary>
    public bool IsRuntimeState => Ownership.IsPreservedOnUpsert();

    /// <summary>
    ///     If true, the attribute is optional, that means it can be null
    /// </summary>
    public bool IsOptional { get; }

    /// <summary>
    ///     Optional meta data of the attribute
    /// </summary>
    public ICollection<CkAttributeMetaDataDto>? MetaData { get; }

    /// <summary>
    ///     An optional description of the attribute
    /// </summary>
    public string? Description { get; }
}