using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.BlueprintCatalogs;

/// <summary>
/// Covers <see cref="BlueprintIdExtensions.IsServiceManaged" />. The Studio mirrors this
/// convention via <c>blueprint-management.ts</c>; both sides MUST stay in lockstep, otherwise
/// the UI hides install for a blueprint nothing manages — or shows install for a blueprint the
/// service immediately reapplies.
/// </summary>
public class BlueprintIdExtensionsTests
{
    [Theory]
    [InlineData("System.Communication-1.0.0")]
    [InlineData("System.Bot-2.1.0")]
    [InlineData("System.Notification-1.0.0")]
    public void IsServiceManaged_ReturnsTrue_ForSystemPrefixedNames(string blueprintId)
    {
        var id = new BlueprintId(blueprintId);
        Assert.True(id.IsServiceManaged());
    }

    [Theory]
    [InlineData("HelloCommunication-1.0.0")]
    [InlineData("InfrastructureStarter-1.0.0")]
    [InlineData("MyCustomBlueprint-1.0.0")]
    public void IsServiceManaged_ReturnsFalse_ForUserInstallableNames(string blueprintId)
    {
        var id = new BlueprintId(blueprintId);
        Assert.False(id.IsServiceManaged());
    }

    [Fact]
    public void IsServiceManaged_IsCaseSensitive()
    {
        // Comparison is StringComparison.Ordinal. A lowercased "system." or similar typo MUST
        // NOT be treated as a system blueprint — otherwise an admin-uploaded blueprint could
        // pose as a service-managed one and have the UI silently hide its controls.
        Assert.False(new BlueprintId("system.Foo-1.0.0").IsServiceManaged());
        Assert.False(new BlueprintId("SYSTEM.Foo-1.0.0").IsServiceManaged());
        Assert.False(new BlueprintId("Systemic.Foo-1.0.0").IsServiceManaged());
    }

    [Fact]
    public void ServiceManagedNamePrefix_StaysAtSystemDot()
    {
        // Pin the literal so a refactor that "tidies up" the prefix can't drift from the Studio
        // side without breaking this test (and the matching blueprint-management.spec.ts).
        Assert.Equal("System.", BlueprintIdExtensions.ServiceManagedNamePrefix);
    }
}
