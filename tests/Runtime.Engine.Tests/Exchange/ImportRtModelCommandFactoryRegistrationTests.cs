using FakeItEasy;
using Meshmakers.Octo.Runtime.Contracts.Exchange;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Exchange;

/// <summary>
/// The blueprint services take their import command from <c>Func&lt;IImportRtModelCommand&gt;</c>, once per import: one
/// command holds the state of the import running on it, so the factory must hand out a new command on every call and
/// never a shared one.
/// </summary>
public class ImportRtModelCommandFactoryRegistrationTests
{
    [Fact]
    public async Task AddRuntimeEngine_RegistersAFactory_ThatHandsOutANewCommandOnEveryCall()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRuntimeEngine();
        // The command's own dependencies are not under test: a transient fake stands in for the engine's command.
        services.AddTransient<IImportRtModelCommand>(_ => A.Fake<IImportRtModelCommand>());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var factory = scope.ServiceProvider.GetRequiredService<Func<IImportRtModelCommand>>();
        var first = factory();
        var second = factory();

        Assert.NotNull(first);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void AddRuntimeEngine_RegistersTheEngineCommandAsTransient()
    {
        var services = new ServiceCollection();
        services.AddRuntimeEngine();

        var registration = Assert.Single(services, d => d.ServiceType == typeof(IImportRtModelCommand));
        Assert.Equal(ServiceLifetime.Transient, registration.Lifetime);
        Assert.Equal(ServiceLifetime.Transient,
            Assert.Single(services, d => d.ServiceType == typeof(Func<IImportRtModelCommand>)).Lifetime);
    }
}
