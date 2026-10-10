using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.Exchange;

/// <summary>
/// What an Upsert import does when the incoming seed value would blank (empty, omit or reset to a
/// default) a non-empty value of an attribute the blueprint does not own (AB#6313, incident AB#6310).
/// </summary>
public enum RtImportBlankingPolicy
{
    /// <summary>
    /// Default. The tenant's existing non-empty value is kept and the case is reported.
    /// </summary>
    Keep = 0,

    /// <summary>
    /// The blanking is applied as the seed says and reported. Only for an explicit operator
    /// confirmation (the preview/confirm flow of AB#6315) - never a default.
    /// </summary>
    Allow = 1,
}

/// <summary>
/// Why a seed value counts as blanking an existing value.
/// </summary>
public enum RtImportBlankingReason
{
    /// <summary>The seed does not blank anything.</summary>
    None = 0,

    /// <summary>The seed declares the attribute with an empty value (null, empty string, empty array, empty record).</summary>
    SeedEmpty = 1,

    /// <summary>The seed does not declare the attribute at all; the full replace would clear it.</summary>
    SeedOmitted = 2,
}

/// <summary>
/// One attribute the guard found blanked by a seed (AB#6313). Reported by
/// <see cref="IImportRtModelCommand.GuardEntries"/> and logged.
/// </summary>
/// <param name="RtId">Runtime id of the entity.</param>
/// <param name="CkTypeId">Runtime CK type id of the entity.</param>
/// <param name="AttributeName">Attribute name (PascalCase, as stored).</param>
/// <param name="Reason">Why the seed value counts as blanking.</param>
/// <param name="Applied">
/// <c>true</c> when the blanking was applied (<see cref="RtImportBlankingPolicy.Allow"/>);
/// <c>false</c> when the existing value was kept.
/// </param>
public sealed record RtImportGuardEntry(
    OctoObjectId RtId,
    RtCkId<CkTypeId> CkTypeId,
    string AttributeName,
    RtImportBlankingReason Reason,
    bool Applied);
