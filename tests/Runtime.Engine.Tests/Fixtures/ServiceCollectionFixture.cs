using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Fixtures;

public class ServiceCollectionFixture
{
    public ServiceCollectionFixture()
    {
        Services = new ServiceCollection();
        Services.AddRuntimeEngine();
        Services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.SetMinimumLevel(LogLevel.Trace);
        });
    }

    public ServiceCollection Services { get; }
}