using Meshmakers.Common.CommandLineParser.Commands;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.BlueprintManager.Tests;

/// <summary>
/// Regression guard for AB#4081. octo-bpm's command parser eager-resolves
/// <see cref="System.Collections.Generic.IEnumerable{T}" /> of <see cref="ICommand" /> at startup,
/// so a single command with an unsatisfiable dependency crashes the whole tool for every invocation
/// — not just for that command. The original crash was a tenant command pulling
/// <c>IBlueprintService -&gt; ITenantBackupService</c> (implemented only in the MongoDB layer the CLI
/// does not reference) into the container. These tests build the real production DI graph via
/// <see cref="Program.ConfigureServices" /> and assert the whole command surface resolves.
/// </summary>
public class CommandResolutionTests
{
    /// <summary>
    /// Builds the production service provider with an empty configuration. All option bindings are
    /// optional, so this mirrors a clean install with no <c>~/.octo-bpm/settings.json</c> and keeps
    /// the test independent of the developer's machine.
    /// </summary>
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        Program.ConfigureServices(services, new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AllRegisteredCommands_Resolve()
    {
        using var provider = BuildProvider();

        // GetServices materializes the IEnumerable<ICommand>, constructing every command — exactly
        // what CommandParser does at startup and precisely where the original crash occurred.
        var commands = provider.GetServices<ICommand>().ToList();

        Assert.NotEmpty(commands);
    }

    [Fact]
    public void Runner_Resolves()
    {
        using var provider = BuildProvider();

        // Mirrors Program.Main: resolving Runner pulls ICommandParser, which eager-resolves the full
        // command list. This is the exact startup path that threw the InvalidOperationException
        // before the fix.
        var runner = provider.GetRequiredService<Runner>();

        Assert.NotNull(runner);
    }

    [Fact]
    public void RegisteredCommands_AreExactlyTheAuthoringCommands()
    {
        using var provider = BuildProvider();

        var commandTypeNames = provider.GetServices<ICommand>()
            .Select(c => c.GetType().Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        string[] expected =
        [
            "CatalogsCommand",
            "ConfigCommand",
            "GetCommand",
            "ListCommand",
            "NewCommand",
            "PackCommand",
            "PublishCommand",
            "UnpublishCommand",
            "ValidateCommand",
            "VersionCommand"
        ];

        Assert.Equal(expected, commandTypeNames);
    }

    [Theory]
    [InlineData("StatusCommand")]
    [InlineData("PreviewCommand")]
    [InlineData("UpdateCommand")]
    [InlineData("HistoryCommand")]
    public void TenantRuntimeCommands_AreNotRegistered(string removedCommandTypeName)
    {
        using var provider = BuildProvider();

        var commandTypeNames = provider.GetServices<ICommand>()
            .Select(c => c.GetType().Name)
            .ToArray();

        // These operate against a tenant and live in octo-cli, not the authoring CLI. Registering
        // them here re-introduces the IBlueprintService -> ITenantBackupService startup crash.
        Assert.DoesNotContain(removedCommandTypeName, commandTypeNames);
    }
}
