using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Meshmakers.Octo.Runtime.Engine.Blueprints;

using Microsoft.Extensions.Logging.Abstractions;
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

    [Theory]
    [InlineData("dev", "Development")]
    [InlineData("test", "Testing")]
    [InlineData("staging", "Staging")]
    [InlineData("production", "Production")]
    [InlineData("DEV", "Development")]            // tolerant of casing in helm value
    [InlineData("  production  ", "Production")]  // tolerant of accidental padding
    public async Task GetVariables_OctoEnvironmentMode_MapsKnownEnvironmentToEnumName(
        string environment, string expectedMode)
    {
        // Pins the kebab-→-PascalCase mapping that lets
        // System.TenantMode-1.0.0 seed `value: "${octo.environmentMode}"` resolve to a
        // System/EnvironmentModes enum-value name. ImportRtModelCommand matches
        // case-insensitively against the enum name, so a single placeholder lands the
        // right numeric key on the entity.
        var provider = MakeProvider(environment: environment);

        var variables = await provider.GetVariablesAsync("tenant", TestContext.Current.CancellationToken);

        Assert.Equal(expectedMode, variables["octo.environmentMode"]);
    }

    [Fact]
    public async Task GetVariables_OctoEnvironmentMode_UnknownEnvironmentFallsBackToDevelopment()
    {
        // Misconfigured or yet-unmapped cluster environment (e.g. "prd" typo, "qa", "e2e"):
        // the provider falls back to Development so the System.TenantMode blueprint apply
        // still lands a valid enum value on the tenant. The fallback is logged at warning
        // level (verified in service logs, not pinned here) so operators can still spot the
        // misconfiguration without it taking the tenant offline.
        var provider = MakeProvider(environment: "prd");

        var variables = await provider.GetVariablesAsync("tenant", TestContext.Current.CancellationToken);

        Assert.Equal("Development", variables["octo.environmentMode"]);
    }

    [Theory]
    [InlineData("3.3.109.0", "3.3.109")]                 // release-tag build: $major.$minor.$build.0
    [InlineData("3.3.109.0-test1", "3.3.109-test1")]     // test-channel build keeps the prerelease tag
    [InlineData("0.1.2606.1001-main", "0.1.2606-main")]  // main-CI build number is 4-segment too
    [InlineData("3.3.109", "3.3.109")]                   // already 3-segment SemVer → untouched
    [InlineData("3.3", "3.3")]                           // shorter than 3 segments → left alone (caller's problem)
    [InlineData("3.3.109+build.42", "3.3.109+build.42")] // 3-segment with build metadata → untouched
    [InlineData("3.3.109.0+build.42", "3.3.109+build.42")] // 4-segment with build metadata → trimmed core
    public async Task GetVariables_OctoVersion_TrimsFourPartVersionToSemVer(
        string rawOctoVersion, string expectedOctoVersion)
    {
        // Helm chart-version validation is strict SemVer (MAJOR.MINOR.PATCH[-pre][+meta]).
        // The OctoMesh Azure-Pipelines convention assembles a 4-segment Build.BuildNumber
        // and feeds it straight to `helm package --app-version`, so .Chart.AppVersion arrives
        // here as e.g. "3.3.109.0". Blueprints that bind System.Communication/ChartVersion
        // to ${octo.version} would otherwise persist an invalid 4-part value and break the
        // very next adapter deploy. Normalisation happens at the variable boundary so the
        // chart's appVersion can stay aligned with the Docker image tag while ${octo.version}
        // is always Helm-safe.
        var provider = MakeProvider(octoVersion: rawOctoVersion);

        var variables = await provider.GetVariablesAsync("tenant", TestContext.Current.CancellationToken);

        Assert.Equal(expectedOctoVersion, variables["octo.version"]);
    }

    [Fact]
    public async Task GetVariables_OctoScheme_DefaultsToHttps()
    {
        // Cluster URL composition relies on ${octo.scheme}://<slug>.${octo.domain}.
        // The scheme defaults to "https" so a host that only configures the domain
        // gets the right protocol without an explicit OCTO_BLUEPRINTS__SCHEME entry.
        var provider = MakeProvider();

        var variables = await provider.GetVariablesAsync("tenant", TestContext.Current.CancellationToken);

        Assert.Equal("https", variables["octo.scheme"]);
    }

    [Fact]
    public async Task GetVariables_OctoScheme_HonoursExplicitOverride()
    {
        // Smoke-test / compose-up environments serve plain HTTP. The override flows
        // straight through without re-validation — operator owns the choice.
        var provider = MakeProvider(scheme: "http");

        var variables = await provider.GetVariablesAsync("tenant", TestContext.Current.CancellationToken);

        Assert.Equal("http", variables["octo.scheme"]);
    }

    [Fact]
    public async Task GetVariables_OctoDomain_DefaultsToEmpty()
    {
        // Local dev (Start-Octo on localhost) leaves the domain unset. Hosts that
        // need explicit per-service URLs (e.g. Identity's RefineryStudioUrl) layer
        // overrides via their own provider; the engine just surfaces what's there.
        var provider = MakeProvider();

        var variables = await provider.GetVariablesAsync("tenant", TestContext.Current.CancellationToken);

        Assert.True(variables.ContainsKey("octo.domain"));
        Assert.Equal(string.Empty, variables["octo.domain"]);
    }

    [Theory]
    [InlineData("test-2.octo-mesh.com", "test-2.octo-mesh.com")]
    [InlineData("test-2.octo-mesh.com/", "test-2.octo-mesh.com")]   // trailing slash stripped
    [InlineData("test-2.octo-mesh.com///", "test-2.octo-mesh.com")] // extra trailing slashes stripped
    public async Task GetVariables_OctoDomain_StripsTrailingSlashes(
        string rawDomain, string expected)
    {
        // Blueprint authors compose ${octo.scheme}://<slug>.${octo.domain}/<path>.
        // Operators paste cluster domain values with or without trailing slashes;
        // stripping them at the provider boundary keeps blueprint authors out of
        // that defensive coding loop.
        var provider = MakeProvider(domain: rawDomain);

        var variables = await provider.GetVariablesAsync("tenant", TestContext.Current.CancellationToken);

        Assert.Equal(expected, variables["octo.domain"]);
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
        string octoVersion = "1.0.0",
        string? scheme = null,
        string? domain = null)
    {
        var options = new OctoBlueprintVariablesOptions
        {
            SystemTenantId = systemTenantId,
            Environment = environment,
            OctoVersion = octoVersion,
        };
        if (scheme != null) options.Scheme = scheme;
        if (domain != null) options.Domain = domain;
        var monitor = new StaticOptionsMonitor<OctoBlueprintVariablesOptions>(options);
        return new DefaultBlueprintVariableProvider(monitor, NullLogger<DefaultBlueprintVariableProvider>.Instance);
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
