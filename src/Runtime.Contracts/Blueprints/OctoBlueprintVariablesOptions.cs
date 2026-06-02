namespace Meshmakers.Octo.Runtime.Contracts.Blueprints;

/// <summary>
///     Configuration for the default <see cref="IBlueprintVariableProvider"/> implementation.
///     Bound from the <c>Blueprints</c> section of the host's configuration (overridable via
///     <c>OCTO_BLUEPRINTS__*</c> environment variables) so the helm chart and per-environment
///     overlays can populate it without code changes.
/// </summary>
public class OctoBlueprintVariablesOptions
{
    /// <summary>
    ///     Default name of the configuration section bound to this options class.
    /// </summary>
    public const string SectionName = "Blueprints";

    /// <summary>
    ///     Default system-tenant identifier. Matches the long-standing OctoMesh convention
    ///     (<c>OctoSystemConfiguration.SystemTenantId</c>'s default value) so blueprints
    ///     referencing <c>${octo.systemTenantId}</c> work without explicit configuration.
    /// </summary>
    public const string DefaultSystemTenantId = "OctoSystem";

    /// <summary>
    ///     Default deployment environment used when nothing is configured. Matches
    ///     <c>ASPNETCORE_ENVIRONMENT=Development</c> semantics — local developer workflows
    ///     should never accidentally see <c>${octo.environment} == "production"</c>.
    /// </summary>
    public const string DefaultEnvironment = "dev";

    /// <summary>
    ///     The current OctoMesh release exposed to blueprints as <c>${octo.version}</c>.
    ///     Typically populated by the helm chart from <c>.Chart.AppVersion</c> and surfaced
    ///     to the container via <c>OCTO_BLUEPRINTS__OCTOVERSION</c>. When empty the variable
    ///     is still exported, but as an empty string — blueprints that depend on it should
    ///     declare a <c>requires:</c> on a non-empty value.
    /// </summary>
    public string OctoVersion { get; set; } = string.Empty;

    /// <summary>
    ///     Deployment environment exposed to blueprints as <c>${octo.environment}</c>.
    ///     Conventional values: <c>dev</c>, <c>test</c>, <c>staging</c>, <c>production</c>.
    ///     Defaults to <see cref="DefaultEnvironment"/>.
    /// </summary>
    public string Environment { get; set; } = DefaultEnvironment;

    /// <summary>
    ///     Tenant identifier treated as the system tenant. Exposed to blueprints as
    ///     <c>${octo.systemTenantId}</c>; the <c>${octo.isSystemTenant}</c> variable is
    ///     <c>"true"</c> when the current <c>tenantId</c> equals this value. Defaults to
    ///     <see cref="DefaultSystemTenantId"/>.
    /// </summary>
    public string SystemTenantId { get; set; } = DefaultSystemTenantId;
}
