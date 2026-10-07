namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     CK v2 (AB#5668): how the generic GraphQL CRUD path may touch an attribute assignment. Declared per
///     assignment (<see cref="CkTypeAttributeDto.Access" />); omitted means <see cref="ReadWrite" />.
/// </summary>
/// <remarks>
///     Phase 0 enforces <see cref="Hidden" /> only (never exposed in GraphQL output or input, rejected by generic
///     mutations). <see cref="ReadOnly" /> and <see cref="MethodOnly" /> are parsed, persisted and carried through
///     the graph; their enforcement follows in a later phase. The value restricts the API surface only — the
///     engine repository, imports and exports still read and write the attribute.
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
}
