using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.Catalog;
using Meshmakers.Octo.ConstructionKit.Engine.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Services;

/// <summary>
///     AB#5453. The dependency half of a hard resolve has always thrown
///     (<c>ModelValidationException.UnknownCkModels</c>), but every other resolve error — an unknown
///     type/record/attribute reference, a duplicate id, a broken display rule, a circular dependency —
///     is only COLLECTED in the <see cref="OperationResult" />. <c>CatalogService.PublishAsync</c> used
///     to log those and <c>return</c>, so <c>octo-ckc publish</c> printed "Construction kit model
///     published" right after the error line and exited 0, and the MSBuild producer task printed its own
///     success message and left the build green — for a model that never reached the catalog.
///     These tests pin the collected-but-not-thrown case specifically: the fake resolver NEVER throws.
/// </summary>
public class CatalogServicePublishTests
{
    private const string CatalogName = "PrivateGitHubCatalog";

    private readonly ICatalogManager _catalogManager = A.Fake<ICatalogManager>();
    private readonly ICatalogModelResolver _catalogModelResolver = A.Fake<ICatalogModelResolver>();
    private readonly CkCompiledModelRoot _model = new() { ModelId = new CkModelId("Industry.Energy", "2.1.1") };
    private readonly OriginFileResolver _originFileResolver = new("ck-industry.energy-2.1.1.yaml");
    private readonly CatalogService _sut;

    public CatalogServicePublishTests()
    {
        _sut = new CatalogService(NullLogger<CatalogService>.Instance, _catalogManager,
            A.Fake<ICkSerializer>(), _catalogModelResolver, A.Fake<ICkCacheService>());
    }

    /// <summary>
    ///     Makes the fake resolver behave like the real one in the quiet failure case: it adds messages
    ///     to the caller's operation result and returns a graph normally, without throwing.
    /// </summary>
    private void ResolveCollects(params OperationMessage[] messages)
    {
        A.CallTo(() => _catalogModelResolver.HardResolveAsync(A<CkCompiledModelRoot>._,
                A<IOriginFileResolver>._, A<OperationResult>._, A<object?>._))
            .ReturnsLazily(call =>
            {
                var operationResult = call.GetArgument<OperationResult>(2)!;
                foreach (var message in messages)
                {
                    operationResult.AddMessage(message);
                }

                return Task.FromResult(new CkModelGraph());
            });
    }

    private void VerifyNothingWasWrittenToAnyCatalog()
    {
        A.CallTo(() => _catalogManager.PublishAsync(A<string>._, A<CkCompiledModelRoot>._, A<bool>._,
            A<object?>._, A<CancellationToken?>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task PublishAsync_WhenResolveCollectsAnErrorWithoutThrowing_ThrowsAndDoesNotPublish()
    {
        ResolveCollects(MessageCodes.UnknownAttributeOfCkTypeIdInSource("types/meter.yaml",
            "Industry.Energy/Obis", "Industry.Energy/Meter"));

        var exception = await Assert.ThrowsAsync<CompilerException>(() =>
            _sut.PublishAsync(CatalogName, _model, _originFileResolver, true));

        VerifyNothingWasWrittenToAnyCatalog();
        Assert.Contains("Industry.Energy-2.1.1", exception.Message);
        Assert.Contains(CatalogName, exception.Message);
        Assert.Contains("Industry.Energy/Obis", exception.Message);
        Assert.True(exception.OperationResult.HasErrors);
    }

    [Fact]
    public async Task PublishAsync_WhenResolveCollectsAFatalErrorWithoutThrowing_ThrowsAndDoesNotPublish()
    {
        ResolveCollects(MessageCodes.CkTypeIdUnknown("types/meter.yaml", "Industry.Basic/Asset"));

        var exception = await Assert.ThrowsAsync<CompilerException>(() =>
            _sut.PublishAsync(CatalogName, _model, _originFileResolver, true));

        VerifyNothingWasWrittenToAnyCatalog();
        Assert.True(exception.OperationResult.HasFatalErrors);
    }

    /// <summary>
    ///     The MSBuild producer task (<c>CkCompile</c>) clears every logging provider, so messages that
    ///     only reach the engine's ILogger are invisible there. It renders the OperationResult instead —
    ///     which is what turns a failed publish into an MSBuild error and fails the producer build.
    /// </summary>
    [Fact]
    public async Task PublishAsync_WithCallerOperationResult_HandsTheResolveMessagesToTheCaller()
    {
        var callerOperationResult = new OperationResult();
        ResolveCollects(MessageCodes.UnknownAttributeOfCkTypeIdInSource("types/meter.yaml",
            "Industry.Energy/Obis", "Industry.Energy/Meter"));

        await Assert.ThrowsAsync<CompilerException>(() =>
            _sut.PublishAsync(CatalogName, _model, _originFileResolver, true, callerOperationResult));

        VerifyNothingWasWrittenToAnyCatalog();
        Assert.True(callerOperationResult.HasErrors);
        Assert.Contains(callerOperationResult.Messages,
            m => m.MessageText.Contains("Industry.Energy/Obis"));
    }

    /// <summary>
    ///     Only the messages this resolve produced may block the publish. The MSBuild task reuses one
    ///     result for the whole construction-kit folder, so an earlier step's message must not be
    ///     re-attributed to the publish — otherwise the second (LocalFileSystemCatalog) publish of the
    ///     same model would fail on the first one's messages.
    /// </summary>
    [Fact]
    public async Task PublishAsync_WithPreExistingErrorFromAnEarlierStep_StillPublishes()
    {
        var callerOperationResult = new OperationResult();
        callerOperationResult.AddMessage(MessageCodes.CkTypeIdUnknown("types/other.yaml", "Other/Type"));
        ResolveCollects();

        await _sut.PublishAsync(CatalogName, _model, _originFileResolver, true, callerOperationResult);

        A.CallTo(() => _catalogManager.PublishAsync(CatalogName, _model, true, null, null))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task PublishAsync_WhenResolveReportsNoError_PublishesToTheCatalog()
    {
        ResolveCollects();

        await _sut.PublishAsync(CatalogName, _model, _originFileResolver, true);

        A.CallTo(() => _catalogManager.PublishAsync(CatalogName, _model, true, null, null))
            .MustHaveHappenedOnceExactly();
    }
}
