namespace Meshmakers.Octo.Runtime.Contracts.Blueprints;

/// <summary>
///     Resolves the set of variables visible to a blueprint when it is applied to a tenant.
///     Variables are referenced from seed-data attribute values and <c>rtWellKnownName</c>
///     via the <c>${name}</c> placeholder syntax, and from the manifest's <c>requires:</c>
///     block to gate whether a blueprint applies at all.
/// </summary>
/// <remarks>
///     The default OctoMesh provider surfaces the following keys per tenant:
///     <list type="bullet">
///         <item><c>octo.version</c> — current OctoMesh release; sourced from
///             <c>OctoBlueprintVariablesOptions.OctoVersion</c> (typically set by the helm
///             chart via the <c>OCTO_BLUEPRINTS__OCTOVERSION</c> environment variable).</item>
///         <item><c>octo.environment</c> — deployment environment (<c>dev</c>, <c>test</c>,
///             <c>staging</c>, <c>production</c>); sourced from
///             <c>OctoBlueprintVariablesOptions.Environment</c>.</item>
///         <item><c>octo.environmentMode</c> — same value mapped to the matching
///             <c>System/EnvironmentModes</c> CK-enum value name (<c>Development</c>,
///             <c>Testing</c>, <c>Staging</c>, <c>Production</c>) so blueprints can seed a
///             <c>System/TenantModeConfiguration</c> entity from a single
///             <c>value: "${octo.environmentMode}"</c> placeholder. Unknown environments
///             pass through unchanged so the runtime import surfaces a clear error.</item>
///         <item><c>octo.tenantId</c> — the tenant currently being initialised.</item>
///         <item><c>octo.systemTenantId</c> — the tenant id treated as system tenant;
///             sourced from <c>OctoBlueprintVariablesOptions.SystemTenantId</c>.</item>
///         <item><c>octo.isSystemTenant</c> — <c>"true"</c> when
///             <c>tenantId == octo.systemTenantId</c>, otherwise <c>"false"</c>.</item>
///     </list>
///     Services may replace the default registration with a richer implementation if they
///     need to surface service-specific facts (chart names, feature flags, …).
/// </remarks>
public interface IBlueprintVariableProvider
{
    /// <summary>
    ///     Returns the variable map for the given tenant. Implementations should treat the
    ///     call as inexpensive — <see cref="IBlueprintService"/> resolves variables once per
    ///     apply and reuses the result for both <c>requires:</c> evaluation and seed-data
    ///     interpolation.
    /// </summary>
    /// <param name="tenantId">Target tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An immutable map of variable name to resolved value.</returns>
    Task<IReadOnlyDictionary<string, string>> GetVariablesAsync(
        string tenantId,
        CancellationToken cancellationToken = default);
}
