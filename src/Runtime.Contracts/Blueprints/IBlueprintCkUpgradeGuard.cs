using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.Blueprints;

/// <summary>
///     Judges, before a blueprint installs or raises CK models in a tenant, which installed models the CK update set
///     would break (tenant upgrade guard, CK v2 F2.5, AB#6871). Read-only: nothing is written.
/// </summary>
/// <remarks>
///     <para>
///         The guard itself (AB#6867) lives with the persistence of the hosting service, because it needs the compiled
///         models of the catalogs and the models persisted in the tenant. The blueprint engine only knows the
///         <em>what</em> - the CK models a blueprint apply or update would install or raise - and asks the host through
///         this interface. A host that registers no implementation runs blueprints unguarded, as before.
///     </para>
///     <para>
///         Implementations decide which models they judge: service-managed models (<c>System</c>, <c>System.*</c>) are
///         never judged by the guard policy (embedded System import stays unguarded).
///     </para>
/// </remarks>
public interface IBlueprintCkUpgradeGuard
{
    /// <summary>
    ///     Judges <paramref name="updateSet" /> as one set against the models installed in the tenant.
    /// </summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="updateSet">
    ///     The concrete CK model versions (<c>name</c> + exact floor version) the blueprint would install or raise,
    ///     already reduced to the ones the tenant does not hold in a satisfying-or-newer version.
    /// </param>
    /// <param name="cancellationToken">Cancels the evaluation.</param>
    /// <returns>The installed models that would break; empty when nothing breaks.</returns>
    /// <exception cref="Exception">
    ///     A failure to judge propagates: the blueprint operation fails closed, it is not silently allowed.
    /// </exception>
    Task<IReadOnlyList<BlueprintCkUpgradeBlocker>> EvaluateAsync(
        string tenantId,
        IReadOnlyList<CkModelId> updateSet,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     One reason why an installed model would break if the CK models of a blueprint were installed (tenant upgrade
///     guard, AB#6867). Language-neutral data plus an English <see cref="Message" />; the same entries the library
///     status reports (AB#6868) and the import endpoints refuse with (AB#6870).
/// </summary>
public sealed class BlueprintCkUpgradeBlocker
{
    /// <summary>The model that would break, full id (for example <c>EnergyCommunity.Registration-2.8.0</c>).</summary>
    public string Dependent { get; init; } = string.Empty;

    /// <summary>The model of the update that causes it, full id (for example <c>Basic-2.5.0</c>).</summary>
    public string Update { get; init; } = string.Empty;

    /// <summary>
    ///     Blocker kind: <c>ExactPin</c>, <c>FloorNotMet</c>, <c>RangeNotSatisfied</c>, <c>ElementRemoved</c>,
    ///     <c>ElementChanged</c>, <c>ElementBecameInternal</c> or <c>NameCollision</c>. Kept as text: new kinds must not
    ///     break clients.
    /// </summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>The pin, range or element / member the blocker is about.</summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>Stable code per kind, for example <c>CK_UPGRADE_EXACT_PIN</c>.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>English explanation naming the dependent.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>True when <see cref="Dependent" /> is itself a model of the evaluated update set.</summary>
    public bool DependentIsInUpdateSet { get; init; }

    /// <summary>A newer version of the dependent that was proven to resolve the blocker; <c>null</c> when there is none.</summary>
    public string? SuggestedDependentUpdate { get; init; }

    /// <summary>Human-readable next step when <see cref="SuggestedDependentUpdate" /> is set; <c>null</c> otherwise.</summary>
    public string? ResolveHint { get; init; }
}

/// <summary>
///     How a blueprint apply or update treats a CK update set that would break an installed model (AB#6871).
/// </summary>
public enum BlueprintCkUpgradeGuardMode
{
    /// <summary>
    ///     No enforcement (the default of the engine API): blueprints applied by the platform itself (service-managed
    ///     blueprints at tenant setup) and existing callers behave as before. A preview still reports the blockers.
    /// </summary>
    Off = 0,

    /// <summary>The operation is refused before the first change when the update set would break an installed model.</summary>
    Enforce = 1,

    /// <summary>
    ///     The operation proceeds although the update set breaks installed models (they become <c>ResolveFailed</c>);
    ///     the blockers are returned in the result so the host can log them with the caller.
    /// </summary>
    ForceBreakingUpdate = 2
}

/// <summary>
///     Options of the <see cref="IBlueprintService" /> apply overload that takes them.
/// </summary>
public class BlueprintApplyOptions
{
    /// <summary>Re-apply the seed data even if the same version is already recorded (recovery).</summary>
    public bool Force { get; set; }

    /// <summary>How a CK update set that would break an installed model is treated. Default: <see cref="BlueprintCkUpgradeGuardMode.Off" />.</summary>
    public BlueprintCkUpgradeGuardMode CkUpgradeGuard { get; set; } = BlueprintCkUpgradeGuardMode.Off;
}
