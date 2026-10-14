using FakeItEasy;
using Meshmakers.Common.Shared.Services;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     AB#6675: <c>octo-ckc -c Compile ... -rr true</c> must keep working (deprecation warning on stderr, option and value
///     dropped before parsing); every other argument passes unchanged.
/// </summary>
public sealed class ObsoleteOptionFilteringEnvironmentServiceTests
{
    private static string[] Filter(params string[] args)
    {
        var inner = A.Fake<IEnvironmentService>();
        A.CallTo(() => inner.GetCommandLineArgs()).Returns(args);
        return new ObsoleteOptionFilteringEnvironmentService(inner).GetCommandLineArgs();
    }

    [Fact]
    public void TheObsoleteOptionAndItsValue_AreRemoved()
    {
        Assert.Equal(["octo-ckc", "-c", "Compile", "-p", "dir", "-o", "out"],
            Filter("octo-ckc", "-c", "Compile", "-p", "dir", "-rr", "true", "-o", "out"));
        Assert.Equal(["octo-ckc", "-c", "Compile"], Filter("octo-ckc", "-c", "Compile", "--rangeRetention", "false"));
        Assert.Equal(["octo-ckc", "-c", "Compile"], Filter("octo-ckc", "-c", "Compile", "-RR", "true"));
    }

    [Fact]
    public void OtherArguments_PassUnchanged_IncludingAValueThatLooksSimilar()
    {
        var args = new[] { "octo-ckc", "-c", "Compile", "-p", "range-retention", "-rrx", "1", "-lce", "false" };

        Assert.Equal(args, Filter(args));
    }

    [Fact]
    public void AValuelessObsoleteOption_DoesNotSwallowTheNextOption()
    {
        Assert.Equal(["octo-ckc", "-c", "Compile", "-p", "dir"], Filter("octo-ckc", "-c", "Compile", "-rr", "-p", "dir"));
    }
}
