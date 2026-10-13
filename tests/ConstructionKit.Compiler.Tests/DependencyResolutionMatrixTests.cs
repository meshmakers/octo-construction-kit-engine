using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     AB#4468: the catalog (compile) side of the SemVer resolution matrix. The same cases run through the
///     repository resolver (<c>RepositoryResolutionMatrixTests</c>, engine tests) and the tenant re-validation
///     (engine-mongodb): a dependency range <c>System-[2.5,3.0)</c> against the versions the catalogs hold.
///     <list type="bullet">
///         <item>a higher minor or patch than the floor resolves and the model compiles;</item>
///         <item>a higher major only, or a version below the floor only, is rejected with a message that names the
///         range and the visible versions;</item>
///         <item>several versions in range: the highest wins.</item>
///     </list>
///     Both with and without range retention (a classic compile resolves the declared range the same way, it only
///     writes the exact pin of the result). No special treatment for System and Meshmakers-shipped models.
/// </summary>
public sealed class DependencyResolutionMatrixTests
{
    public static TheoryData<bool, string[], string?> Cases()
    {
        var data = new TheoryData<bool, string[], string?>();
        foreach (var rangeRetention in new[] { true, false })
        {
            data.Add(rangeRetention, ["2.5.0"], "System-2.5.0");
            data.Add(rangeRetention, ["2.6.1"], "System-2.6.1");
            data.Add(rangeRetention, ["2.4.9"], null);
            data.Add(rangeRetention, ["3.0.0"], null);
            data.Add(rangeRetention, ["2.5.0", "2.6.1", "3.0.0"], "System-2.6.1");
            data.Add(rangeRetention, ["2.5.0", "2.5.7", "2.9.0", "3.0.0"], "System-2.9.0");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task RangeAgainstAvailableVersions_ResolvesOrIsRejectedWithRangeAndVersions(bool rangeRetention,
        string[] available, string? expectedResolved)
    {
        using var fixture = new CkCompileFixture(s =>
            s.Configure<CkCompilerOptions>(o => o.RangeRetention = rangeRetention));
        foreach (var version in available)
        {
            await fixture.CompileAndPublishAsync(fixture.WriteSystemModel(version));
        }

        var source = fixture.WriteSource("dep", "Dep-1.0.0", ["System-[2.5,3.0)"], new Dictionary<string, string>
        {
            ["types/thing.yaml"] = "types:\n  - typeId: Thing\n    derivedFromCkTypeId: ${System}/Entity\n"
        });

        if (expectedResolved != null)
        {
            var compiled = await fixture.CompileAsync(source);
            Assert.Equal([expectedResolved], compiled.Dependencies!.Select(d => d.FullName));
            if (rangeRetention)
            {
                var range = Assert.Single(compiled.DependencyRanges!);
                Assert.Equal("System-[2.5,3.0)", range.Range.FullName);
            }
        }
        else
        {
            var exception = await Assert.ThrowsAsync<ModelValidationException>(() => fixture.CompileAsync(source));
            Assert.Contains("System-[2.5,3.0)", exception.Message);
            foreach (var version in available)
            {
                Assert.Contains(version, exception.Message);
            }
        }
    }
}
