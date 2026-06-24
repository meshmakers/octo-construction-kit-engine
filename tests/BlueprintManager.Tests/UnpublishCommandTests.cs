using FakeItEasy;
using Meshmakers.Octo.BlueprintManager.Commands.Implementations;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.BlueprintManager.Tests;

/// <summary>
/// Behavioural tests for <see cref="UnpublishCommand" />. Arguments are driven through the real argument
/// parser (<c>ParseLayer</c>) and the catalog manager is faked, so these assert the command's force gating,
/// single-vs-all-versions routing, and idempotent no-op without touching any catalog.
/// </summary>
public class UnpublishCommandTests
{
    private const string Catalog = LocalFileSystemBlueprintCatalog.Name;

    [Fact]
    public async Task Execute_WithoutForce_DoesNotCallManager()
    {
        var manager = ManagerWith(("MyBlueprint", "1.0.0"));
        var cmd = CreateCommand(manager);
        cmd.CommandArgumentValue.ParseLayer(["-b", "MyBlueprint", "-r", "1.0.0"]);

        await cmd.Execute();

        A.CallTo(() => manager.UnpublishAsync(A<string>._, A<BlueprintId>._, A<object?>._, A<CancellationToken?>._))
            .MustNotHaveHappened();
        A.CallTo(() => manager.UnpublishAllVersionsAsync(A<string>._, A<string>._, A<object?>._, A<CancellationToken?>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Execute_WithForce_SingleVersion_CallsUnpublishAsync()
    {
        var manager = ManagerWith(("MyBlueprint", "1.0.0"));
        var cmd = CreateCommand(manager);
        cmd.CommandArgumentValue.ParseLayer(["-b", "MyBlueprint", "-r", "1.0.0", "-f"]);

        await cmd.Execute();

        A.CallTo(() => manager.UnpublishAsync(Catalog,
                A<BlueprintId>.That.Matches(b => b.FullName == "MyBlueprint-1.0.0"),
                A<object?>._, A<CancellationToken?>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Execute_WithForce_NoVersion_CallsUnpublishAllVersions()
    {
        var manager = ManagerWith(("MyBlueprint", "1.0.0"), ("MyBlueprint", "1.1.0"));
        var cmd = CreateCommand(manager);
        cmd.CommandArgumentValue.ParseLayer(["-b", "MyBlueprint", "-f"]);

        await cmd.Execute();

        A.CallTo(() => manager.UnpublishAllVersionsAsync(Catalog, "MyBlueprint", A<object?>._, A<CancellationToken?>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Execute_WithForce_CallsManagerEvenWhenIndexEmpty()
    {
        // Simulates the post-publish GitHub-Pages-lag window: the listing shows nothing yet, but --force
        // must still remove directly via the authoritative (and idempotent) catalog layer rather than
        // falsely reporting "nothing to unpublish".
        var manager = ManagerWith(); // empty listing
        var cmd = CreateCommand(manager);
        cmd.CommandArgumentValue.ParseLayer(["-b", "MyBlueprint", "-r", "1.0.0", "-f"]);

        await cmd.Execute();

        A.CallTo(() => manager.UnpublishAsync(Catalog,
                A<BlueprintId>.That.Matches(b => b.FullName == "MyBlueprint-1.0.0"), A<object?>._, A<CancellationToken?>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Execute_WithoutForce_EmptyIndex_DoesNotCallManager()
    {
        var manager = ManagerWith(); // empty listing
        var cmd = CreateCommand(manager);
        cmd.CommandArgumentValue.ParseLayer(["-b", "MyBlueprint", "-r", "1.0.0"]);

        await cmd.Execute();

        A.CallTo(() => manager.UnpublishAsync(A<string>._, A<BlueprintId>._, A<object?>._, A<CancellationToken?>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Execute_DryRun_NormalizesVersion_PreviewsCanonicalEntry()
    {
        // The catalog stores the canonical "1.2.0"; the user asks for "1.2". The dry-run preview must
        // normalize before comparing, otherwise it reports "nothing matches" while --force would delete it.
        var manager = ManagerWith(("MyBlueprint", "1.2.0"));
        var logger = new CapturingLogger<UnpublishCommand>();
        var cmd = new UnpublishCommand(logger, Options.Create(new BpmToolOptions()), manager);
        cmd.CommandArgumentValue.ParseLayer(["-b", "MyBlueprint", "-r", "1.2"]); // no --force → dry run

        await cmd.Execute();

        Assert.Contains(logger.Messages, m => m.Contains("MyBlueprint-1.2.0"));
        Assert.DoesNotContain(logger.Messages, m => m.Contains("nothing matches"));
    }

    [Fact]
    public async Task Execute_InvalidVersion_LogsErrorAndDoesNotCallManager()
    {
        var manager = ManagerWith(("MyBlueprint", "1.0.0"));
        var logger = new CapturingLogger<UnpublishCommand>();
        var cmd = new UnpublishCommand(logger, Options.Create(new BpmToolOptions()), manager);
        cmd.CommandArgumentValue.ParseLayer(["-b", "MyBlueprint", "-r", "not-a-version", "-f"]);

        await cmd.Execute();

        Assert.Contains(logger.Messages, m => m.Contains("Invalid version"));
        A.CallTo(() => manager.UnpublishAsync(A<string>._, A<BlueprintId>._, A<object?>._, A<CancellationToken?>._))
            .MustNotHaveHappened();
    }

    private static IBlueprintCatalogManager ManagerWith(params (string name, string version)[] items)
    {
        var manager = A.Fake<IBlueprintCatalogManager>();
        var result = new BlueprintListResult
        {
            Items = items.Select(i => new BlueprintCatalogResultItem
            {
                CatalogName = Catalog,
                BlueprintId = new BlueprintId(i.name, i.version),
                Description = "d"
            }).ToList()
        };
        A.CallTo(() => manager.ListAsync(A<int>._, A<int>._, A<object?>._, A<CancellationToken?>._)).Returns(result);
        return manager;
    }

    private static UnpublishCommand CreateCommand(IBlueprintCatalogManager manager)
        => new(NullLogger<UnpublishCommand>.Instance, Options.Create(new BpmToolOptions()), manager);
}

/// <summary>
/// Minimal <see cref="ILogger{T}" /> that records the formatted log messages, so tests can assert on
/// command output. (FakeItEasy cannot proxy <c>ILogger&lt;internal-type&gt;</c>, hence a hand-rolled double.)
/// </summary>
internal sealed class CapturingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    public List<string> Messages { get; } = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
        TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Messages.Add(formatter(state, exception));

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
