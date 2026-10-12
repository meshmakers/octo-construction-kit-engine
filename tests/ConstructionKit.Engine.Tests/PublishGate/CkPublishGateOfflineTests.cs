using System.Net;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using Octokit;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.PublishGate;

/// <summary>
///     AB#6582: an unreachable catalog during <c>Publish</c> ends as <c>OCTO-CK202</c> (exit <c>-6</c>) wherever the gate
///     reads - not as an unhandled <see cref="HttpRequestException" /> (exit <c>-99</c>) - and a transport failure in the
///     middle of the write is a publish failure with a readable message. <c>ValidateCatalog</c> opens the pinned model
///     file instead of trusting the index.
/// </summary>
public sealed class CkPublishGateOfflineTests : IDisposable
{
    private const string Catalog = RepositoryBackedCatalog.Name;
    private readonly GateHarness _h = new();

    public void Dispose() => _h.Dispose();

    private InMemoryGitHubRepository Repo => _h.Repository;

    private static CkCompiledModelRoot V2(string version, string enumValues = "A,B", string? name = null) =>
        GateModels.Create(version, enumValues, 2, name);

    private static CkCompiledModelRoot Classic(string name, string version, CkModelId? pin = null) =>
        new() { ModelId = new CkModelId(name, version), Description = name, Dependencies = pin == null ? [] : [pin] };

    public static TheoryData<string> ReadFailures =>
        new() { "offline", "rate limit", "timeout" };

    private static Exception Failure(string kind) => kind switch
    {
        "offline" => new HttpRequestException("Connection refused (127.0.0.1:9)"),
        "timeout" => new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"),
        _ => ModelCatalogException.GitHubRequestFailed("read", "withdrawn.json", 403, "API rate limit exceeded", 6,
            new ApiException("rate limit", HttpStatusCode.Forbidden))
    };

    [Theory]
    [MemberData(nameof(ReadFailures))]
    public async Task An_unreadable_withdrawn_record_is_CK202_not_an_unhandled_exception(string kind)
    {
        await _h.PublishAsync(V2("1.0.0"), Catalog);
        // Everything but the burned-numbers record can be read: the failure sits in the last read of the gate.
        Repo.ApiFault = path => path.EndsWith("withdrawn.json", StringComparison.Ordinal) ? Failure(kind) : null;
        var before = Repo.Snapshot();

        var result = await _h.EvaluateAsync(V2("1.1.0", "A,B,C"), Catalog);
        var exception = await Assert.ThrowsAsync<CkPublishRefusedException>(() =>
            _h.PublishAsync(V2("1.1.0", "A,B,C"), Catalog));

        Assert.Equal(CkPublishGateDecision.Refused, result.Decision);
        Assert.Equal(["OCTO-CK202"], GateHarness.ErrorCodes(result));
        Assert.Contains("OCTO-CK202", exception.AllText());
        Assert.Equal(before, Repo.Snapshot());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_transport_failure_while_writing_is_a_publish_failure_with_a_readable_message(bool timeout)
    {
        await _h.PublishAsync(V2("1.0.0"), Catalog);
        Repo.WriteFault = _ => timeout
            ? new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout")
            : new HttpRequestException("Connection refused (127.0.0.1:9)");

        var exception = await Assert.ThrowsAsync<ModelCatalogException>(() =>
            _h.PublishAsync(V2("1.1.0", "A,B,C"), Catalog));

        Assert.Contains("Publishing model 'GateModel-1.1.0'", exception.Message);
        Assert.Contains(timeout ? "canceled" : "Connection refused", exception.Message);
        Assert.False(Repo.Files.ContainsKey("ck-models/v3/g/GateModel/1/ck-gatemodel-1.1.0.json"));
    }

    [Fact]
    public async Task A_transport_failure_while_unpublishing_is_an_unpublish_failure_with_a_readable_message()
    {
        await _h.PublishAsync(V2("1.0.0"), Catalog);
        Repo.WriteFault = _ => new HttpRequestException("Connection refused (127.0.0.1:9)");

        var exception = await Assert.ThrowsAsync<ModelCatalogException>(() =>
            _h.CatalogService.UnpublishAsync(Catalog, new CkModelId("GateModel", "1.0.0"), null));

        Assert.Contains("Unpublishing model 'GateModel-1.0.0'", exception.Message);
    }

    private async Task<CkModelId> PublishProviderAndConsumerAsync()
    {
        var provider = new CkModelId("ProviderModel", "1.7.2");
        await _h.PublishAsync(Classic("ProviderModel", "1.7.2"), Catalog);
        await _h.PublishAsync(Classic("ConsumerModel", "1.0.0", provider), Catalog);
        _h.ClearCatalogCache();
        _h.Restart(); // a fresh process: no cached index, no model payload read by the publish
        return provider;
    }

    [Fact]
    public async Task ValidateCatalog_resolves_a_pin_whose_file_is_there()
    {
        await PublishProviderAndConsumerAsync();

        var result = await _h.Get<ICkCatalogValidationService>().ValidateAsync(Catalog);

        Assert.Equal(0, result.BrokenCount);
        Assert.False(result.HasDefects);
    }

    [Fact]
    public async Task ValidateCatalog_flags_a_pinned_model_file_that_was_deleted_while_its_index_entry_remains()
    {
        await PublishProviderAndConsumerAsync();
        Repo.Files.Remove("ck-models/v2/p/ProviderModel/1/ck-providermodel-1.7.2.json"); // by hand, index entry kept
        Assert.Contains("1.7.2", Repo.Files["ck-models/v2/p/ProviderModel/1/catalog.json"]);

        var result = await _h.Get<ICkCatalogValidationService>().ValidateAsync(Catalog);

        var consumer = result.Models.Single(m => m.Model.Name == "ConsumerModel");
        Assert.False(consumer.IsResolved);
        var finding = Assert.Single(consumer.Findings);
        Assert.Contains("ProviderModel-1.7.2", finding);
        Assert.Contains("could not be loaded", finding);
        Assert.True(result.HasDefects);
    }
}
