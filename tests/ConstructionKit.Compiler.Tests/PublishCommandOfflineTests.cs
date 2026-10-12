using FakeItEasy;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     AB#6580 / AB#6582: <c>octo-ckc -c Publish</c> against a GitHub catalog that cannot be read - the process an operator
///     sees: the real command parser, the real <see cref="Runner" />, the real <see cref="GitHubCatalog" /> reader. The
///     catalog's repository is offline (every call throws what the HTTP stack throws) or does not exist. Expected: nothing is
///     written, <c>OCTO-CK202</c> in the log and exit code <c>-6</c> - not an unhandled exception and <c>-99</c> (exit 157).
/// </summary>
public sealed class PublishCommandOfflineTests : IDisposable
{
    private const string OfflineCatalogName = "OfflineGitHubCatalog";
    private readonly IEnvironmentService _environment = A.Fake<IEnvironmentService>();
    private readonly IGitHubClientWrapper _gitHub = A.Fake<IGitHubClientWrapper>();
    private readonly CkCompileFixture _fixture;

    public PublishCommandOfflineTests()
    {
        var cacheDirectory = Path.Combine(Path.GetTempPath(), $"OfflineCatalogCache_{Guid.NewGuid():N}");
        Directory.CreateDirectory(cacheDirectory);
        _cacheDirectory = cacheDirectory;
        _fixture = new CkCompileFixture(services =>
        {
            services.AddSingleton<ICatalog>(sp => new OfflineGitHubCatalog(sp.GetRequiredService<ICkJsonSerializer>(),
                A.Fake<IHttpClientFactory>(), new FixedClientFactory(_gitHub), cacheDirectory));
            services.AddSingleton(_environment);
            services.AddSingleton(A.Fake<IConsoleService>());
            services.AddSingleton<IParserService, ParserService>();
            services.AddSingleton<ICommandParser, CommandParser>();
            services.AddTransient<ICommand, PublishCommand>();
        });
    }

    private readonly string _cacheDirectory;

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_cacheDirectory))
        {
            Directory.Delete(_cacheDirectory, true);
        }
    }

    private async Task<string> CompiledModelAsync()
    {
        var source = _fixture.WriteSource("offline", "OfflineModel-1.0.0", null, new Dictionary<string, string>
        {
            ["attributes/attributes.yaml"] = "attributes:\n  - id: Serial\n    valueType: String\n"
        }, 2);
        var operationResult = new OperationResult();
        var result = await _fixture.Services.GetRequiredService<ICompilerService>()
            .CompileAsync(source, Path.Combine(_fixture.Root, "out"), null, operationResult);
        Assert.False(operationResult.HasErrors);
        return result.CompiledModelFile;
    }

    private async Task<(int ExitCode, List<string> Lines)> RunPublishAsync()
    {
        var file = await CompiledModelAsync();
        A.CallTo(() => _environment.GetCommandLineArgs())
            .Returns(["octo-ckc", "-c", "Publish", "-f", file, "-c", OfflineCatalogName]);
        var logger = new CapturingLogger<Runner>();
        var exitCode = await new Runner(logger, _fixture.Services.GetRequiredService<ICommandParser>()).DoActionAsync();
        return (exitCode, logger.Lines);
    }

    [Fact]
    public async Task An_offline_catalog_ends_with_CK202_and_exit_minus_6_not_with_an_unhandled_exception()
    {
        A.CallTo(_gitHub).Throws(new HttpRequestException("Connection refused (127.0.0.1:9)"));

        var (exitCode, lines) = await RunPublishAsync();

        Assert.Equal(-6, exitCode);
        Assert.Contains(lines, l => l.Contains("OCTO-CK202") && l.Contains("Connection refused"));
        A.CallTo(() => _gitHub.CreateFileAsync(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        A.CallTo(() => _gitHub.UpsertFileWithMergeAsync(A<string>._, A<string>._, A<Func<string?, string?>>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task A_repository_that_does_not_exist_ends_with_CK202_and_exit_minus_6_not_with_a_first_publication()
    {
        A.CallTo(() => _gitHub.GetRepositoryStateAsync()).Returns(GitHubRepositoryState.RepositoryNotFound);

        var (exitCode, lines) = await RunPublishAsync();

        Assert.Equal(-6, exitCode);
        Assert.Contains(lines, l => l.Contains("OCTO-CK202") && l.Contains("does not exist"));
        A.CallTo(() => _gitHub.CreateFileAsync(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
    }

    private sealed class FixedClientFactory(IGitHubClientWrapper client) : IGitHubClientFactory
    {
        public IGitHubClientWrapper CreateClient(IGitHubOptions gitHubOptions) => client;
    }

    private sealed class OfflineGitHubCatalog(ICkJsonSerializer serializer, IHttpClientFactory httpClientFactory,
        IGitHubClientFactory gitHubClientFactory, string cacheDirectory)
        : GitHubCatalog(serializer, httpClientFactory, gitHubClientFactory, Options(cacheDirectory), 15, OfflineCatalogName,
            "Offline GitHub catalog (tests)")
    {
        private static PrivateGitHubCatalogOptions Options(string cacheDirectory) => new()
        {
            IsEnabled = true,
            CacheDirectory = cacheDirectory,
            GitHubPagesUri = "https://test.github.io/offline",
            GitHubRepositoryOwner = "meshmakers",
            GitHubRepositoryName = "offline-catalog",
            GitHubRepositoryBranch = "main",
            GitHubApiToken = "t",
            RefreshNotFoundRetryCount = 0,
            RefreshNotFoundRetryDelaySeconds = 0
        };
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                Lines.Add(formatter(state, exception));
            }
        }
    }
}
