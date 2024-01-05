using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Compiler.SystemTests.Fixtures;

public class ServiceCollectionFixture
{
    public ServiceCollectionFixture()
    {
        Services = new ServiceCollection();
        Services.AddConstructionKit();
        Services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.SetMinimumLevel(LogLevel.Trace);
        });
    }

    public ServiceCollection Services { get; }
}