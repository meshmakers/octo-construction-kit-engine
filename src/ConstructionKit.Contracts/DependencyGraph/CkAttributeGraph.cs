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
        Ownership = AttributeOwnership.Resolve(attributeDto.Ownership, attributeDto.IsRuntimeState);
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
    /// <param name="description">An optional description to the attribute</param>
    /// <param name="metaData">Optional meta data of the attribute</param>
    /// <param name="isRuntimeState">
    /// DEPRECATED alias for <see cref="Ownership"/>, kept so CK caches and callers that pre-date
    /// AB#5187 keep working: it seeds <see cref="Ownership"/> (true →
    /// <see cref="AttributeOwnershipDto.RuntimeState"/>, false →
    /// <see cref="AttributeOwnershipDto.SeedOwned"/>) and a deserialized <c>ownership</c> key,
    /// when present, then overwrites it.
    /// Trailing + defaulted so existing positional callers (e.g. the mesh-adapter SDK tests
    /// that instantiate <see cref="CkTypeAttributeGraph"/> / <see cref="CkAttributeGraph"/>
    /// directly) compile unchanged. STJ binds the constructor by parameter name, so the JSON
    /// wire format is unaffected.
    /// </param>
    [JsonConstructor]
    public CkAttributeGraph(CkId<CkAttributeId> ckAttributeId, AttributeValueTypesDto valueType, CkId<CkRecordId>? valueCkRecordId,
        CkId<CkEnumId>? valueCkEnumId, ICollection<object>? defaultValues, string? description,
        ICollection<CkAttributeMetaDataDto>? metaData, bool isRuntimeState = false)
    {
        CkAttributeId = ckAttributeId;
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
    ///     Resolved ownership of the attribute definition (AB#5187): the declared
    ///     <see cref="CkAttributeDto.Ownership"/>, or the deprecated <c>isRuntimeState</c> alias
    ///     mapped onto it. Never null — an undeclared attribute resolves to
    ///     <see cref="AttributeOwnershipDto.SeedOwned"/>.
    /// </summary>
    public AttributeOwnershipDto Ownership { get; set; }

    /// <summary>
    ///     DEPRECATED mirror of <see cref="Ownership"/> meaning "preserved on Upsert". Kept for
    ///     the JSON cache wire format and for consumers that pre-date AB#5187; new code asks
    ///     <see cref="Ownership"/> the specific question it has
    ///     (<see cref="AttributeOwnership.IsPreservedOnUpsert"/> /
    ///     <see cref="AttributeOwnership.IsExcludedFromExport"/>).
    /// </summary>
    public bool IsRuntimeState => Ownership.IsPreservedOnUpsert();

    /// <summary>
    ///     An optional description of the attribute
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    ///     Optional meta data of the attribute
    /// </summary>
    public ICollection<CkAttributeMetaDataDto>? MetaData { get; }
}