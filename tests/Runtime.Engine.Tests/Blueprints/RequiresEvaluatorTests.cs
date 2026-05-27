using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.Runtime.Engine.Blueprints;

using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Blueprints;

/// <summary>
/// Unit tests for <see cref="RequiresEvaluator"/>. Pins the matching semantics so the
/// blueprint-skip path behaves identically across YAML scalar/sequence input shapes and
/// across services that register a custom <c>IBlueprintVariableProvider</c>.
/// </summary>
public class RequiresEvaluatorTests
{
    [Fact]
    public void Match_AllRequiresSatisfied_ReturnsNull()
    {
        var requires = new RequiresMap
        {
            ["octo.environment"] = ["staging", "production"],
            ["octo.isSystemTenant"] = ["false"],
        };
        var variables = Vars(
            ("octo.environment", "production"),
            ("octo.isSystemTenant", "false"));

        Assert.Null(RequiresEvaluator.FindMismatch(requires, variables));
    }

    [Fact]
    public void Match_VariableNotInAllowList_ReturnsReason()
    {
        // Returning a non-null reason — rather than just a bool — is what lets the
        // BlueprintService surface a useful skip message to the operator.
        var requires = new RequiresMap
        {
            ["octo.environment"] = ["production"],
        };
        var variables = Vars(("octo.environment", "dev"));

        var reason = RequiresEvaluator.FindMismatch(requires, variables);

        Assert.NotNull(reason);
        Assert.Contains("octo.environment", reason);
        Assert.Contains("'dev'", reason);
        Assert.Contains("production", reason);
    }

    [Fact]
    public void Match_VariableMissingFromContext_ReturnsReason()
    {
        // A required variable that isn't even surfaced by the provider is a configuration
        // error — fail closed instead of treating "missing" as "anything goes".
        var requires = new RequiresMap
        {
            ["octo.featureFlag"] = ["enabled"],
        };
        var variables = Vars(); // no octo.featureFlag

        var reason = RequiresEvaluator.FindMismatch(requires, variables);

        Assert.NotNull(reason);
        Assert.Contains("octo.featureFlag", reason);
        Assert.Contains("not defined", reason);
    }

    [Fact]
    public void Match_EmptyAllowList_AlwaysFails()
    {
        // YAML authors who write `key: []` almost certainly meant to write something
        // else; rejecting it loudly catches the typo early.
        var requires = new RequiresMap
        {
            ["octo.environment"] = [],
        };
        var variables = Vars(("octo.environment", "production"));

        var reason = RequiresEvaluator.FindMismatch(requires, variables);

        Assert.NotNull(reason);
        Assert.Contains("empty allow-list", reason);
    }

    [Fact]
    public void Match_CaseSensitive_MismatchedCasingFails()
    {
        // Ordinal compare — blueprint authors must normalise casing themselves. Pinned in
        // a test because changing to InvariantCulture would silently break installs that
        // gate on (e.g.) tenant ids whose casing already matters elsewhere.
        var requires = new RequiresMap
        {
            ["octo.isSystemTenant"] = ["true"],
        };
        var variables = Vars(("octo.isSystemTenant", "True"));

        Assert.NotNull(RequiresEvaluator.FindMismatch(requires, variables));
    }

    private static Dictionary<string, string> Vars(params (string key, string value)[] entries)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            dict[key] = value;
        }
        return dict;
    }
}
