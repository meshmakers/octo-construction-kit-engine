namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     CK v2 (AB#5668): how the generic GraphQL CRUD path may touch an attribute assignment. Declared per
///     assignment (<see cref="CkTypeAttributeDto.Access" />); omitted means <see cref="ReadWrite" />.
/// </summary>
/// <remarks>
///     The engine parses, persists and carries the value through the graph; the API layer (asset-repo GraphQL)
///     enforces it — <see cref="Hidden" /> is never exposed in output or input. The compiler keeps Hidden out of
///     display rules, indexes, auto-complete values and association roles (messages 105–108). The engine
///     repository itself still reads and writes the attribute (CK methods run through it), but the RT exchange paths
///     honour the access too (AB#7767): <c>ExportRt</c> never emits a <see cref="Hidden" /> value, and an import,
///     blueprint seed, blueprint update or migration Add step refuses a value for a <see cref="Hidden" /> or
///     <see cref="MethodOnly" /> attribute (message 139).
/// </remarks>
public enum CkAttributeAccessDto
{
    /// <summary>
    ///     Default: readable and writable through generic queries and mutations.
    /// </summary>
    ReadWrite = 0,

    /// <summary>
    ///     Readable; writable through generic mutations only on create.
    /// </summary>
    ReadOnly = 1,

    /// <summary>
    ///     Readable; written only by CK methods, never by generic mutations.
    /// </summary>
    MethodOnly = 2,

    /// <summary>
    ///     Never exposed through GraphQL output or input (e.g. password hashes, security stamps).
    /// </summary>
    Hidden = 3
}

/// <summary>
///     Resolution and behaviour predicates for <see cref="CkAttributeAccessDto" /> (CK v2, AB#5668). Consumers go
///     through these instead of testing the enum inline.
/// </summary>
public static class AttributeAccess
{
    /// <summary>
    ///     Effective access of an assignment: the declared value, otherwise <see cref="CkAttributeAccessDto.ReadWrite" />.
    /// </summary>
    public static CkAttributeAccessDto Resolve(CkAttributeAccessDto? assignment) =>
        assignment ?? CkAttributeAccessDto.ReadWrite;

    /// <summary>
    ///     True when the attribute appears in GraphQL output types (everything except <see cref="CkAttributeAccessDto.Hidden" />).
    /// </summary>
    public static bool IsExposedInOutput(CkAttributeAccessDto access) => access != CkAttributeAccessDto.Hidden;

    /// <summary>
    ///     True when the attribute appears in generic GraphQL input types (<see cref="CkAttributeAccessDto.ReadWrite" />
    ///     and <see cref="CkAttributeAccessDto.ReadOnly" />).
    /// </summary>
    public static bool IsExposedInGenericInput(CkAttributeAccessDto access) =>
        access is CkAttributeAccessDto.ReadWrite or CkAttributeAccessDto.ReadOnly;

    /// <summary>
    ///     True when a generic mutation may write the attribute: always for <see cref="CkAttributeAccessDto.ReadWrite" />,
    ///     on create only for <see cref="CkAttributeAccessDto.ReadOnly" />.
    /// </summary>
    public static bool IsGenericallyWritable(CkAttributeAccessDto access, bool isCreate) =>
        access == CkAttributeAccessDto.ReadWrite || (isCreate && access == CkAttributeAccessDto.ReadOnly);

    /// <summary>
    ///     AB#7767: true when a value may be read outside a CK method — in a generic query, an RT export, a pipeline
    ///     or any other tool (everything except <see cref="CkAttributeAccessDto.Hidden" />). Same rule as
    ///     <see cref="IsExposedInOutput" />, named for the question the non-GraphQL consumers ask.
    /// </summary>
    public static bool IsReadableOutsideMethod(CkAttributeAccessDto access) => IsExposedInOutput(access);

    /// <summary>
    ///     AB#7767: true when a value may be written outside a CK method — by an RT import, a blueprint seed or a
    ///     migration step (<see cref="CkAttributeAccessDto.ReadWrite" /> and <see cref="CkAttributeAccessDto.ReadOnly" />).
    ///     <see cref="CkAttributeAccessDto.MethodOnly" /> and <see cref="CkAttributeAccessDto.Hidden" /> are written by
    ///     CK methods only. Unlike <see cref="IsGenericallyWritable" /> it does not distinguish create from update:
    ///     an import has no such split, and ReadOnly stays importable.
    /// </summary>
    public static bool IsWritableOutsideMethod(CkAttributeAccessDto access) => IsExposedInGenericInput(access);
}
