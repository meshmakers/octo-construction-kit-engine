using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Meshmakers.Octo.Runtime.Engine.Blueprints;

using Microsoft.Extensions.Options;

using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Blueprints;

/// <summary>
/// Unit tests for <see cref="DefaultBlueprintVariableProvider"/>. Covers the contract
/// documented on <see cref="IBlueprintVariableProvider"/>: the five standard
/// <c>octo.*</c> keys are surfaced per tenant, with <c>octo.isSystemTenant</c> derived
/// from <c>octo.systemTenantId</c>.
/// </summary>
public class DefaultBlueprintVariableProviderTests
{
    [Fact]
    public async Task GetVariables_IsSystemTenant_ReportsTrueForMatchingTenant()
    {
        var provider = MakeProvider(systemTenantId: "OctoSystem");

        var variables = await provider.GetVariablesAsync("OctoSystem", TestContext.Current.CancellationToken);

        Assert.Equal("true", variables["octo.isSystemTenant"]);
        Assert.Equal("OctoSystem", variables["octo.tenantId"]);
        Assert.Equal("OctoSystem", variables["octo.systemTenantId"]);
    }

    [Fact]
    public async Task GetVariables_IsSystemTenant_TolerantOfCasingDifference()
    {
        // Tenant ids are normalised to lowercase at the system context layer
        // (StringExtensions.NormalizeString = Trim().ToLower()), so callers pass
        // "octosystem" while helm-injected SystemTenantId is "OctoSystem". The
        // mismatch was the root cause of every tenant — including OctoSystem —
        // receiving TenantCockpit instead of SystemCockpit on the first deploy.
        var provider = MakeProvider(systemTenantId: "OctoSystem");

        var variables = await provider.GetVariablesAsync("octosystem", TestContext.Current.CancellationToken);

        Assert.Equal("true", variables["octo.isSystemTenant"]);
    }

    [Fact]
    public async Task GetVariables_IsSystemTenant_ReportsFalseForOtherTenant()
    {
        var provider = MakeProvider(systemTenantId: "OctoSystem");

        var variables = await provider.GetVariablesAsync("customer-acme", TestContext.Current.CancellationToken);

        Assert.Equal("false", variables["octo.isSystemTenant"]);
        Assert.Equal("customer-acme", variables["octo.tenantId"]);
    }

    [Fact]
    public async Task GetVariables_SurfacesEnvironmentAndVersionFromOptions()
    {
        var provider = MakeProvider(
            systemTenantId: "OctoSystem",
            environment: "production",
            octoVersion: "3.5.2");

        var variables = await provider.GetVariablesAsync("customer-acme", TestContext.Current.CancellationToken);

        Assert.Equal("production", variables["octo.environment"]);
        Assert.Equal("3.5.2", variables["octo.version"]);
    }

    [Fact]
    public async Task GetVariables_OctoVersionEmpty_StillExportsKey()
    {
        // Helm chart hasn't injected OCTO_BLUEPRINTS__OCTOVERSION → we surface an empty
        // string so blueprints can detect the missing value via a `requires:` block
        // (e.g. octo.version: [non-empty]) rather than failing late with an unknown-var
        // warning during interpolation.
        var provider = MakeProvider(octoVersion: string.Empty);

        var variables = await provider.GetVariablesAsync("tenant", TestContext.Current.CancellationToken);

        Assert.True(variables.ContainsKey("octo.version"));
        Assert.Equal(string.Empty, variables["octo.version"]);
    }

    private static DefaultBlueprintVariableProvider MakeProvider(
        string systemTenantId = "OctoSystem",
        string environment = "dev",
        string octoVersion = "1.0.0")
    {
        var options = new OctoBlueprintVariablesOptions
        {
            SystemTenantId = systemTenantId,
            Environment = environment,
            OctoVersion = octoVersion,
        };
        var monitor = new StaticOptionsMonitor<OctoBlueprintVariablesOptions>(options);
        return new DefaultBlueprintVariableProvider(monitor);
    }

    /// <summary>
    /// Minimal IOptionsMonitor implementation that returns a fixed snapshot. The default
    /// provider only reads CurrentValue, so we don't need the change-token machinery.
    /// </summary>
    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value) { CurrentValue = value; }
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
