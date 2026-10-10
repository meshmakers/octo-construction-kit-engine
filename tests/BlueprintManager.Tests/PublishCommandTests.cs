using FakeItEasy;
using Meshmakers.Octo.BlueprintManager.Commands.Implementations;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.BlueprintManager.Tests;

/// <summary>
/// Regression guard for the publish default-catalog bug: PublishCommand used to default <c>-c</c> to the
/// literal "LocalFileSystemCatalog", which does not match the registered catalog name
/// (<see cref="LocalFileSystemBlueprintCatalog.Name" /> = "LocalFileSystemBlueprintCatalog") and threw
/// CatalogNotFound when <c>-c</c> was omitted. Locks the default to the constant.
/// </summary>
public class PublishCommandTests
{
    [Fact]
    public async Task Execute_WithoutCatalogArg_PublishesToLocalFileSystemBlueprintCatalog()
    {
        var manager = A.Fake<IBlueprintCatalogManager>();
        var compiler = A.Fake<IBlueprintCompilerService>();
        var meta = new BlueprintMetaRootDto { BlueprintId = new BlueprintId("MyBlueprint", "1.0.0") };
        A.CallTo(() => compiler.ValidateAsync(A<string>._, A<OperationResult>._, A<CancellationToken>._)).Returns(meta);
        // not already present → command proceeds to publish (no --force needed)
        A.CallTo(() => manager.IsExistingAsync(A<string>._, A<BlueprintId>._, A<object?>._)).Returns(false);

        var cmd = new PublishCommand(NullLogger<PublishCommand>.Instance, Options.Create(new BpmToolOptions()),
            manager, compiler);
        cmd.CommandArgumentValue.ParseLayer(["-p", "/tmp/does-not-need-to-exist"]); // no -c

        await cmd.Execute();

        A.CallTo(() => manager.PublishAsync(LocalFileSystemBlueprintCatalog.Name, meta, A<string>._, false,
                A<object?>._, A<CancellationToken?>._))
            .MustHaveHappenedOnceExactly();
    }

    // ---- AB#6397: existence check per TARGET catalog -------------------------------------------------------
    // The manager answers per catalog name (the real per-catalog logic is covered in
    // BlueprintCatalogManagerTests); here the command must ask for the TARGET catalog and decide on that alone.

    private const string PublicName = "PublicGitHubBlueprintCatalog";
    private const string PrivateName = "PrivateGitHubBlueprintCatalog";

    [Theory]
    [InlineData(false, false, true)] // none has it -> publish to target
    [InlineData(false, true, true)]  // only the OTHER catalog has it -> publish (the AB#6397 bug)
    [InlineData(true, false, false)] // the target has it -> blocked without --force
    [InlineData(true, true, false)]  // both have it -> blocked (target has it)
    public async Task Execute_DecidesOnTargetCatalogOnly(bool targetHasIt, bool otherHasIt, bool expectPublish)
    {
        var meta = new BlueprintMetaRootDto { BlueprintId = new BlueprintId("Base", "2.11.1") };
        var manager = A.Fake<IBlueprintCatalogManager>();
        A.CallTo(() => manager.IsExistingAsync(PublicName, A<BlueprintId>._, A<object?>._)).Returns(targetHasIt);
        A.CallTo(() => manager.IsExistingAsync(PrivateName, A<BlueprintId>._, A<object?>._)).Returns(otherHasIt);
        // the catalog-wide answer is "any catalog has it" - it must no longer drive the publish decision
        A.CallTo(() => manager.IsExistingAsync(A<BlueprintId>._, A<object?>._)).Returns(targetHasIt || otherHasIt);

        await RunPublish(manager, meta, PublicName, force: false);

        if (expectPublish)
        {
            A.CallTo(() => manager.PublishAsync(PublicName, meta, A<string>._, false, A<object?>._,
                A<CancellationToken?>._)).MustHaveHappenedOnceExactly();
        }
        else
        {
            A.CallTo(() => manager.PublishAsync(A<string>._, A<BlueprintMetaRootDto>._, A<string>._, A<bool>._,
                A<object?>._, A<CancellationToken?>._)).MustNotHaveHappened();
        }

        A.CallTo(() => manager.PublishAsync(PrivateName, A<BlueprintMetaRootDto>._, A<string>._, A<bool>._,
            A<object?>._, A<CancellationToken?>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Execute_WithForce_ReplacesExistingInTargetCatalog()
    {
        var meta = new BlueprintMetaRootDto { BlueprintId = new BlueprintId("Base", "2.11.1") };
        var manager = A.Fake<IBlueprintCatalogManager>();
        A.CallTo(() => manager.IsExistingAsync(PublicName, A<BlueprintId>._, A<object?>._)).Returns(true);

        await RunPublish(manager, meta, PublicName, force: true);

        A.CallTo(() => manager.PublishAsync(PublicName, meta, A<string>._, true, A<object?>._,
            A<CancellationToken?>._)).MustHaveHappenedOnceExactly();
    }

    private static async Task RunPublish(IBlueprintCatalogManager manager, BlueprintMetaRootDto meta,
        string catalogName, bool force)
    {
        var compiler = A.Fake<IBlueprintCompilerService>();
        A.CallTo(() => compiler.ValidateAsync(A<string>._, A<OperationResult>._, A<CancellationToken>._)).Returns(meta);

        var cmd = new PublishCommand(NullLogger<PublishCommand>.Instance, Options.Create(new BpmToolOptions()),
            manager, compiler);
        cmd.CommandArgumentValue.ParseLayer(force
            ? ["-p", "/tmp/does-not-need-to-exist", "-c", catalogName, "-f"]
            : ["-p", "/tmp/does-not-need-to-exist", "-c", catalogName]);

        await cmd.Execute();
    }
}
