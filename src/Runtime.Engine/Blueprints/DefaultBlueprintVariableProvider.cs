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
        var systemTenantId = snapshot.SystemTenantId;
        var isSystemTenant = string.Equals(tenantId, systemTenantId, StringComparison.Ordinal);

        IReadOnlyDictionary<string, string> variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["octo.version"] = snapshot.OctoVersion ?? string.Empty,
            ["octo.environment"] = snapshot.Environment ?? OctoBlueprintVariablesOptions.DefaultEnvironment,
            ["octo.tenantId"] = tenantId,
            ["octo.systemTenantId"] = systemTenantId ?? OctoBlueprintVariablesOptions.DefaultSystemTenantId,
            ["octo.isSystemTenant"] = isSystemTenant ? "true" : "false",
        };

        return Task.FromResult(variables);
    }
}
