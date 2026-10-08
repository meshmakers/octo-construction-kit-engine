namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     CK v2 (F1.1-S4, concept §4.2): whether an element may be referenced from another model. Declared on types,
///     records, enums, attribute definitions, interfaces, association roles and methods; omitted means
///     <see cref="Public" /> for every CK language version.
/// </summary>
public enum CkVisibilityDto
{
    /// <summary>
    ///     Default: may be referenced from any model; part of the model's public surface.
    /// </summary>
    Public = 0,

    /// <summary>
    ///     May only be referenced inside the declaring model; not part of the compatibility surface.
    /// </summary>
    Internal = 1
}

/// <summary>
///     CK v2 (F1.1-S4, concept §4.2, decision 6): who may derive from a type or record. Omitted means
///     <see cref="Any" /> in a <c>ckLanguage: 1</c> model and <see cref="Model" /> in a <c>ckLanguage: 2</c> model.
/// </summary>
public enum CkDerivableDto
{
    /// <summary>
    ///     Any model may derive from the element (the v1 behaviour).
    /// </summary>
    Any = 0,

    /// <summary>
    ///     Only the declaring model may derive from the element; sealed for every other model.
    /// </summary>
    Model = 1
}

/// <summary>
///     Resolution of the declared (nullable) modifiers to their effective values.
/// </summary>
public static class CkModifiers
{
    /// <summary>The effective visibility: the declared value, otherwise <see cref="CkVisibilityDto.Public" />.</summary>
    public static CkVisibilityDto ResolveVisibility(CkVisibilityDto? declared) => declared ?? CkVisibilityDto.Public;

    /// <summary>
    ///     The effective derivability: the declared value, otherwise <see cref="CkDerivableDto.Model" /> for a
    ///     <c>ckLanguage: 2</c> model and <see cref="CkDerivableDto.Any" /> for a <c>ckLanguage: 1</c> model.
    /// </summary>
    public static CkDerivableDto ResolveDerivable(CkDerivableDto? declared, int effectiveCkLanguage) =>
        declared ?? (effectiveCkLanguage >= 2 ? CkDerivableDto.Model : CkDerivableDto.Any);
}
