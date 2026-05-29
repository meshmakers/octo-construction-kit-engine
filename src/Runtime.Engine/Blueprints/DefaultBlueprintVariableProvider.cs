using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
///     Default <see cref="IBlueprintVariableProvider"/> backed by
///     <see cref="OctoBlueprintVariablesOptions"/>. Surfaces the OctoMesh-standard
///     <c>octo.*</c> variables without any service-specific knowledge — services that need
///     additional facts (chart names, feature flags, secret indirection) should replace this
///     registration in DI with a richer provider.
/// </summary>
internal sealed class DefaultBlueprintVariableProvider : IBlueprintVariableProvider
{
    private readonly IOptionsMonitor<OctoBlueprintVariablesOptions> _options;

    public DefaultBlueprintVariableProvider(IOptionsMonitor<OctoBlueprintVariablesOptions> options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> GetVariablesAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = _options.CurrentValue;
        var systemTenantId = snapshot.SystemTenantId ?? OctoBlueprintVariablesOptions.DefaultSystemTenantId;
        var environment = snapshot.Environment ?? OctoBlueprintVariablesOptions.DefaultEnvironment;

        // Tenant ids are normalised to lowercase at the system context layer
        // (StringExtensions.NormalizeString → Trim().ToLower()), so the configured
        // SystemTenantId (e.g. helm-injected "OctoSystem") will arrive here as
        // "octosystem" at runtime. Compare case-insensitively so the configured value
        // can be written in its display form without breaking the match — the same
        // tolerance every other tenant-id check in the platform relies on.
        var isSystemTenant = string.Equals(tenantId, systemTenantId, StringComparison.OrdinalIgnoreCase);

        IReadOnlyDictionary<string, string> variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["octo.version"] = snapshot.OctoVersion ?? string.Empty,
            ["octo.environment"] = environment,
            ["octo.environmentMode"] = MapEnvironmentToMode(environment),
            ["octo.tenantId"] = tenantId,
            ["octo.systemTenantId"] = systemTenantId,
            ["octo.isSystemTenant"] = isSystemTenant ? "true" : "false",
        };

        return Task.FromResult(variables);
    }

    /// <summary>
    /// Maps the helm-injected <c>octo.environment</c> token (kebab-friendly: dev/test/
    /// staging/production) to the matching <c>System/EnvironmentModes</c> CK-enum value
    /// name (PascalCase: Development/Testing/Staging/Production). Blueprints that seed a
    /// <c>System/TenantModeConfiguration</c> can write
    /// <c>value: "${octo.environmentMode}"</c> and rely on
    /// <c>ImportRtModelCommand</c>'s tolerant enum-value match (Key | Key.ToString() |
    /// Name OrdinalIgnoreCase) to land the correct numeric key on the entity.
    /// Unknown environments pass through unchanged so the importer fails loudly with
    /// "unknown enum value '&lt;raw&gt;'" rather than silently defaulting to Development.
    /// </summary>
    private static string MapEnvironmentToMode(string environment)
    {
        return environment.Trim().ToLowerInvariant() switch
        {
            "dev" => "Development",
            "test" => "Testing",
            "staging" => "Staging",
            "production" => "Production",
            _ => environment,
        };
    }
}
