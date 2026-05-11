using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// DB-neutral description of a single archive column derived from a <c>CkArchiveColumn.Path</c>
/// resolved against a target CK type. Consumed by the DDL generator (CrateDB) and by validation.
/// </summary>
/// <param name="Path">
/// The original attribute path from <c>CkArchiveColumn.Path</c>, e.g. <c>"voltage"</c>,
/// <c>"sensor.reading.value"</c>, or <c>"readings[*].value"</c>.
/// </param>
/// <param name="ColumnName">
/// The storage column name. Currently equal to <see cref="Path"/> (preserved with dots, quoted
/// during DDL emission); kept as a separate field so future renaming logic stays type-safe.
/// </param>
/// <param name="PrimitiveType">
/// The leaf primitive type when <see cref="IsRecord"/> is false. <c>null</c> when the path
/// terminates on a record attribute.
/// </param>
/// <param name="IsRecord">True when the path terminates on a <c>Record</c> attribute.</param>
/// <param name="IsArray">True when the path traverses or terminates on a <c>RecordArray</c> or
/// <c>StringArray</c> attribute (or contains <c>[*]</c> projection).</param>
/// <param name="RecordTypeId">
/// The CK record id when <see cref="IsRecord"/> is true; <c>null</c> otherwise.
/// </param>
/// <param name="InheritedFromCkTypeId">
/// The CK type id from which the leaf attribute is inherited; <c>null</c> when the attribute
/// belongs to the resolved target type itself.
/// </param>
public record ArchiveColumnSpec(
    string Path,
    string ColumnName,
    AttributeValueTypesDto? PrimitiveType,
    bool IsRecord,
    bool IsArray,
    RtCkId<CkRecordId>? RecordTypeId,
    RtCkId<CkTypeId>? InheritedFromCkTypeId);
