using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.CkModelMigrations;

/// <summary>
/// Records noteworthy events that occur during CK model imports. Bridged at deployment time
/// to the platform notification/event repository via an adapter (analogous to
/// <c>IArchiveAuditTrail</c>); kept as a focused interface so the engine has no direct
/// dependency on the notifications package.
/// </summary>
public interface ICkModelImportAuditTrail
{
    /// <summary>
    /// Records that a custom extension value of an extensible enum overrode a value defined by
    /// the imported CK model with the same key. The custom extension value takes precedence
    /// (see WI #3324 acceptance criteria).
    /// </summary>
    /// <param name="tenantId">Tenant whose extensible enum was affected. <c>null</c> denotes the
    /// system tenant (e.g. import of system models).</param>
    /// <param name="ckModelId">The CK model that was imported.</param>
    /// <param name="ckEnumId">The affected extensible enum.</param>
    /// <param name="ckEnumValueName">Name of the CK-defined value that was overridden.</param>
    /// <param name="extensionValueName">Name of the custom extension value that took
    /// precedence.</param>
    /// <param name="extensionValueKey">Numeric key shared by the CK-defined and the extension
    /// value (the cause of the collision).</param>
    Task RecordExtensibleEnumValueOverrideAsync(
        string? tenantId,
        CkModelId ckModelId,
        CkId<CkEnumId> ckEnumId,
        string ckEnumValueName,
        string extensionValueName,
        int extensionValueKey);
}
