using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Compiler.SystemTests.Fixtures;

public class ServiceCollectionFixture
{
    public ServiceCollection Services { get; }

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

    

}