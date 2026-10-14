using FakeItEasy;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.RangeRetention;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     AB#6683 / AB#6675: the range audit (<c>octo-ckc -c AuditRanges</c>) asks the SAME rule the range-retaining
///     compile enforces. <see cref="Rule_AgreesWithTheRangeRetainingCompile" /> proves it against the real compiler for
///     every range shape the audit of 2026-10-14 found; the other tests pin the audit itself and the command.
/// </summary>
public sealed class RangeAuditTests : IDisposable
{
    private readonly CkCompileFixture _on = new(s => s.Configure<CkCompilerOptions>(o => o.RangeRetention = true));
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"RangeAudit_{Guid.NewGuid():N}");

    public void Dispose()
    {
        _on.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    public static TheoryData<string, bool> Ranges() => new()
    {
        { "System-[2.5,3.0)", true },
        { "System-[2.5,2.9)", true },
        { "System-(2.4,3.0)", true },
        { "System-[2.5.0]", true },
        { "System-[2.5,)", false }, // unbounded (Loxone: Basic-[2.4,))
        { "System-2.5", false }, // >= 2.5.0
        { "System", false }, // bare model name = >= 1.0.0 (System.Sdk, OctoEnergyDemo)
        { "System-[2.5,3.0]", false }, // the next major itself is inside
        { "System-[2.5,4.0)", false } // two majors
    };

    [Theory]
    [MemberData(nameof(Ranges))]
    public async Task Rule_AgreesWithTheRangeRetainingCompile(string declared, bool expectedWithinOneMajor)
    {
        foreach (var version in new[] { "2.5.0", "2.6.0" })
        {
            await _on.CompileAndPublishAsync(_on.WriteSystemModel(version, withExtraAttribute: true));
        }

        Assert.Equal(expectedWithinOneMajor, CkDependencyRangeRule.IsWithinOneMajor(new CkModelIdVersionRange(declared)));

        var source = _on.WriteSource("dep", "Dep-1.0.0", [declared], new Dictionary<string, string>
        {
            ["types/thing.yaml"] = "types:\n  - typeId: Thing\n    derivedFromCkTypeId: ${System}/Entity\n"
        });
        if (expectedWithinOneMajor)
        {
            var compiled = await _on.CompileAsync(source);
            Assert.NotNull(compiled.DependencyRanges);
        }
        else
        {
            var exception = await Assert.ThrowsAsync<ModelValidationException>(() => _on.CompileAsync(source));
            Assert.Contains("admit more than one major version", exception.Message);
        }
    }

    private void WriteModel(string relativeDirectory, string modelId, params string[] dependencies)
    {
        var dir = Path.Combine(_root, relativeDirectory);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "ckModel.yaml"),
            "\"$schema\": \"https://schemas.meshmakers.cloud/construction-kit-meta.schema.json\"\n" +
            (dependencies.Length == 0 ? "" : "dependencies:\n" + string.Concat(dependencies.Select(d => $"- {d}\n"))) +
            $"modelId: {modelId}\n");
    }

    [Fact]
    public async Task Audit_FindsEveryCkModelYaml_ReportsEachDeclaration_AndSkipsBuildOutput()
    {
        WriteModel("repoA/src/Good/ConstructionKit", "Good-1.0.0", "System-[2.5,3.0)", "Basic-[2.4,3.0)");
        WriteModel("repoB/src/Loxone/ConstructionKit", "Loxone-5.3.0", "System-[2.5,3.0)", "Basic-[2.4,)");
        WriteModel("repoC/src/Sdk/ConstructionKit", "System.Sdk", "System");
        WriteModel("repoD/src/NoDeps/ConstructionKit", "NoDeps-1.0.0");
        WriteModel("repoB/src/Loxone/bin/DebugL/net10.0", "Copy-1.0.0", "System-[2.0,)"); // build output: skipped
        var audit = new CkModelRangeAudit(_on.Services.GetRequiredService<ICkSerializer>());

        var entries = await audit.AuditAsync(_root);

        Assert.Equal(5, entries.Count);
        // A bare model name reads as >=1.0.0, so it is listed as System-1.0.0.
        Assert.Equal(["Basic-[2.4,)", "System-1.0.0"], entries.Where(e => !e.IsWithinOneMajor).Select(e => e.Range.FullName).Order());
        Assert.Equal(3, entries.Count(e => e.IsWithinOneMajor)); // Good (2) + Loxone System-[2.5,3.0)
        Assert.DoesNotContain(entries, e => e.File.Contains("bin"));
        Assert.Contains(entries, e => e.ModelId.StartsWith("System.Sdk", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Audit_ReportsAnUnreadableFile_WithoutStopping()
    {
        WriteModel("ok", "Ok-1.0.0", "System-[2.5,3.0)");
        Directory.CreateDirectory(Path.Combine(_root, "broken"));
        File.WriteAllText(Path.Combine(_root, "broken", "ckModel.yaml"), "dependencies: [\n  - : :");
        var unreadable = new List<string>();

        var entries = await new CkModelRangeAudit(_on.Services.GetRequiredService<ICkSerializer>())
            .AuditAsync(_root, unreadable);

        Assert.Single(entries);
        Assert.Single(unreadable);
    }

    [Fact]
    public async Task Command_ExitsWithAnErrorNamingTheRejectedRanges_AndPassesWhenAllAreBounded()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddConstructionKit();
        var environment = A.Fake<IEnvironmentService>();
        services.AddSingleton(environment);
        services.AddSingleton(A.Fake<IConsoleService>());
        services.AddSingleton<IParserService, ParserService>();
        services.AddSingleton<ICommandParser, CommandParser>();
        services.AddTransient<ICommand, AuditRangesCommand>();
        using var provider = services.BuildServiceProvider();
        Task Run(string path)
        {
            A.CallTo(() => environment.GetCommandLineArgs()).Returns(["octo-ckc", "-c", "AuditRanges", "-p", path]);
            return provider.GetRequiredService<ICommandParser>().ParseAndValidateAsync();
        }

        WriteModel("good", "Good-1.0.0", "System-[2.5,3.0)");
        await Run(Path.Combine(_root, "good")); // no exception

        WriteModel("bad", "Bad-1.0.0", "Basic-[2.4,)");
        var exception = await Assert.ThrowsAsync<ModelValidationException>(() => Run(Path.Combine(_root, "bad")));
        Assert.Contains("Bad-1.0.0 -> Basic-[2.4,)", exception.Message);
    }
}
