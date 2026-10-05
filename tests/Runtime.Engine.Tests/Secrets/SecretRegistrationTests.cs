using Meshmakers.Octo.Runtime.Contracts.Secrets;
using Meshmakers.Octo.Runtime.Engine.Repositories;
using Meshmakers.Octo.Runtime.Engine.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Secrets;

/// <summary>
///     AB#5532: AddRuntimeEngine registers the write step, wires it into BulkRtMutation and registers the
///     sweep service.
/// </summary>
public class SecretRegistrationTests
{
    [Fact]
    public void AddRuntimeEngine_RegistersWriteStepAndSweep()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddRuntimeEngine();
        using var provider = services.BuildServiceProvider();

        var normalizer = provider.GetRequiredService<ISecretWriteNormalizer>();
        Assert.IsType<SecretWriteNormalizer>(normalizer);
        Assert.Same(normalizer, provider.GetRequiredService<IBulkRtMutation>().SecretWriteNormalizer);
        Assert.NotNull(provider.GetRequiredService<ISecretMaintenanceService>());
    }
}
