namespace Meshmakers.Octo.ConstructionKit.Contracts.SemVer;

/// <summary>
///     Kind of a structural change between two compiled construction kit models.
/// </summary>
public enum CkModelChangeKind
{
    /// <summary>
    ///     The element exists in the current model but not in the baseline.
    /// </summary>
    Added,

    /// <summary>
    ///     The element exists in the baseline model but not in the current one.
    /// </summary>
    Removed,

    /// <summary>
    ///     The element exists in both models but a property differs.
    /// </summary>
    Modified
}

/// <summary>
///     The kind of construction kit model element a change refers to.
/// </summary>
public enum CkModelElementKind
{
    /// <summary>
    ///     The model root itself (e.g. its description).
    /// </summary>
    Model,

    /// <summary>
    ///     A dependency of the model to another construction kit model.
    /// </summary>
    Dependency,

    /// <summary>
    ///     A construction kit type.
    /// </summary>
    Type,

    /// <summary>
    ///     An attribute assignment on a type.
    /// </summary>
    TypeAttribute,

    /// <summary>
    ///     An association assignment on a type.
    /// </summary>
    TypeAssociation,

    /// <summary>
    ///     An index definition on a type.
    /// </summary>
    TypeIndex,

    /// <summary>
    ///     A construction kit attribute definition.
    /// </summary>
    Attribute,

    /// <summary>
    ///     A construction kit enum definition.
    /// </summary>
    Enum,

    /// <summary>
    ///     A single value of a construction kit enum.
    /// </summary>
    EnumValue,

    /// <summary>
    ///     A construction kit record definition.
    /// </summary>
    Record,

    /// <summary>
    ///     An attribute assignment on a record.
    /// </summary>
    RecordAttribute,

    /// <summary>
    ///     A construction kit association role definition.
    /// </summary>
    AssociationRole,

    /// <summary>
    ///     An attribute assignment on an association role.
    /// </summary>
    AssociationRoleAttribute
}

/// <summary>
///     A single typed structural change between two compiled construction kit models
///     (baseline = last published version, current = locally compiled model).
/// </summary>
public sealed record CkModelChange
{
    /// <summary>
    ///     The kind of change (added, removed, modified).
    /// </summary>
    public required CkModelChangeKind ChangeKind { get; init; }

    /// <summary>
    ///     The kind of element the change refers to.
    /// </summary>
    public required CkModelElementKind ElementKind { get; init; }

    /// <summary>
    ///     The id (path) of the changed element, e.g. <c>Machine-1</c> for a type or
    ///     <c>Machine-1/SerialNumber</c> for an attribute assignment on a type.
    /// </summary>
    public required string ElementId { get; init; }

    /// <summary>
    ///     For <see cref="CkModelChangeKind.Modified" /> changes the name of the property that
    ///     changed (camelCase as written in the model YAML, e.g. <c>valueType</c>); otherwise null.
    /// </summary>
    public string? Property { get; init; }

    /// <summary>
    ///     Rendered old value (baseline model); null when not applicable.
    /// </summary>
    public string? OldValue { get; init; }

    /// <summary>
    ///     Rendered new value (current model); null when not applicable.
    /// </summary>
    public string? NewValue { get; init; }
}
