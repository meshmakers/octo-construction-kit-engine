using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Xunit.Sdk;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.PublishGate;

/// <summary>
///     AB#6386 "v1 replay": every published model version of a catalog replayed in release order through the gate
///     into a copy (an in-memory repository). The gate writes nothing; the test lists the releases today's verdict
///     would refuse and pins them to the known historical set. Opt-in because
///     it needs a downloaded copy of the published files (read-only HTTP GETs of the public catalog; never a publish):
///     set <c>OCTO_PUBLISH_GATE_REPLAY_DIR</c> to a directory with <c>index.json</c>
///     (<c>[{model, version, publishedAt, path}]</c>) and the files below <c>files/</c>;
///     <c>OCTO_PUBLISH_GATE_REPLAY_DIR_REPORT</c> names an optional report file.
/// </summary>
public sealed class PublishGateReplayTests : IDisposable
{
    private const string Variable = "OCTO_PUBLISH_GATE_REPLAY_DIR";
    private readonly GateHarness _h = new();

    public void Dispose() => _h.Dispose();

    private sealed record Entry(string Model, string Version, DateTime PublishedAt, string Path);

    /// <summary>
    ///     Releases of the public catalog (state 2026-10-11) that predate today's rules and that today's classifier
    ///     refuses: early patch/minor releases that changed a dependency pin or added elements. The latest release of
    ///     every model replays clean; a change of this list means the verdict changed.
    /// </summary>
    private static readonly string[] KnownHistoricalRefusals =
    [
        "Basic-2.0.2", "Demo.Tickets-1.0.1", "FamilyOs-1.0.1", "Industry.Basic-2.1.1", "Industry.Basic-2.1.3",
        "Industry.Manufacturing-2.1.1", "Meshmakers.Accounting-2.4.0", "Meshmakers.Accounting-2.4.1",
        "OctoSdkDemo-1.0.1", "System-2.0.7", "System-2.0.8", "System-2.0.9", "System-2.2.2", "System.Bot-3.1.1",
        "System.Communication-3.25.0", "System.Identity-2.11.0", "System.StreamData-1.7.0", "System.UI-2.2.0"
    ];

    [Fact]
    public async Task Replaying_the_published_catalog_in_release_order_refuses_only_the_known_historical_releases()
    {
        var directory = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrEmpty(directory))
        {
            Assert.Skip($"Opt-in: set {Variable} to a downloaded copy of a published catalog.");
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var entries = JsonSerializer.Deserialize<List<Entry>>(
                await File.ReadAllTextAsync(Path.Combine(directory, "index.json"), TestContext.Current.CancellationToken),
                options)!
            .OrderBy(e => e.PublishedAt).ThenBy(e => new CkVersion(e.Version)).ToList();
        var serializer = _h.Get<ICkJsonSerializer>();

        var refused = new List<string>();
        var unchanged = 0;
        foreach (var entry in entries)
        {
            var original = await File.ReadAllTextAsync(Path.Combine(directory, "files", entry.Path),
                TestContext.Current.CancellationToken);
            await using var stream = File.OpenRead(Path.Combine(directory, "files", entry.Path));
            var operationResult = new OperationResult();
            var model = await serializer.DeserializeCompiledModelRootAsync(stream, entry.Path, operationResult, true);
            Assert.False(operationResult.HasErrors, entry.Path);

            // The gate judges the release against the copy as it stands; the history then goes on regardless (the
            // dependency resolve of a full publish needs releases that are no longer in the catalog).
            var before = _h.Repository.Snapshot();
            var result = await _h.EvaluateAsync(model, RepositoryBackedCatalog.Name);
            Assert.Equal(before, _h.Repository.Snapshot()); // the gate itself writes nothing
            if (result.Decision == Engine.SemVer.CkPublishGateDecision.Refused)
            {
                refused.Add($"{model.ModelId.FullName}: " +
                            string.Join(" | ", result.Messages.Where(m => m.Code != null).Select(m => $"{m.Code} {m.Text}")));
            }

            await _h.SeedAsync(model, RepositoryBackedCatalog.Name);
            var file = Path.GetFileName(entry.Path);
            if (_h.Repository.Files.Single(f => f.Key.EndsWith("/" + file, StringComparison.Ordinal) &&
                                                f.Key.Contains("/" + model.ModelId.Name + "/")).Value == original)
            {
                unchanged++;
            }
        }

        var unexpected = refused.Where(r => !KnownHistoricalRefusals.Any(k => r.StartsWith(k + ":"))).ToList();
        var summary = $"REPLAY: {entries.Count} versions, {refused.Count} refused by today's verdict, " +
                      $"{unchanged} files byte-identical to the originals" + Environment.NewLine +
                      string.Join(Environment.NewLine, refused.Select(r => "REFUSED: " + r));
        var report = Environment.GetEnvironmentVariable(Variable + "_REPORT");
        if (!string.IsNullOrEmpty(report))
        {
            await File.WriteAllTextAsync(report, summary, TestContext.Current.CancellationToken);
        }

        Assert.True(unexpected.Count == 0, summary);
    }
}
