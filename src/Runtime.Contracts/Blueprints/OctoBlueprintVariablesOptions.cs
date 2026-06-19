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

    /// <summary>
    ///     URL scheme used when blueprints compose service URLs from
    ///     <see cref="Domain"/>. Exposed as <c>${octo.scheme}</c>. Defaults to
    ///     <c>"https"</c>; set to <c>"http"</c> in plain-HTTP environments
    ///     (compose-up smoke tests).
    /// </summary>
    /// <remarks>
    ///     This pair of variables (<see cref="Scheme"/> + <see cref="Domain"/>) lets
    ///     blueprints compose per-service public URLs from a single per-cluster
    ///     domain instead of carrying one explicit URL setting per service. The
    ///     authoritative pattern is <c>${octo.scheme}://&lt;slug&gt;.${octo.domain}</c>.
    ///     Hosts that need per-service overrides (e.g. dev environments where
    ///     services run on localhost with different ports) layer them on top
    ///     through the host-specific variable provider.
    /// </remarks>
    public string Scheme { get; set; } = "https";

    /// <summary>
    ///     Public DNS suffix for service URLs in cluster deployments. Exposed
    ///     as <c>${octo.domain}</c>. Empty by default — local dev environments
    ///     and hosts that do not use the per-cluster domain pattern leave it
    ///     unset and rely on per-service overrides. Trailing slashes are
    ///     stripped on the way out.
    /// </summary>
    /// <remarks>
    ///     Example values: <c>"test-2.octo-mesh.com"</c>, <c>"staging-1.octo-mesh.com"</c>.
    ///     Blueprints that depend on this variable being non-empty should
    ///     declare a <c>requires:</c> guard so they fail loudly when the host
    ///     forgets to set it instead of materialising an entity with a
    ///     half-composed URL.
    /// </remarks>
    public string Domain { get; set; } = string.Empty;
}
