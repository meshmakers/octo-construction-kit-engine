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
        A.CallTo(() => manager.IsExistingAsync(A<BlueprintId>._, A<object?>._)).Returns(false);

        var cmd = new PublishCommand(NullLogger<PublishCommand>.Instance, Options.Create(new BpmToolOptions()),
            manager, compiler);
        cmd.CommandArgumentValue.ParseLayer(["-p", "/tmp/does-not-need-to-exist"]); // no -c

        await cmd.Execute();

        A.CallTo(() => manager.PublishAsync(LocalFileSystemBlueprintCatalog.Name, meta, A<string>._, false,
                A<object?>._, A<CancellationToken?>._))
            .MustHaveHappenedOnceExactly();
    }
}
