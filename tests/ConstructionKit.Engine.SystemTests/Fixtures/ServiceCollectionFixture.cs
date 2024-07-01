using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConstructionKit.Engine.SystemTests.Fixtures;

public class ServiceCollectionFixture
{
    public ServiceCollectionFixture()
    {
        Services = new ServiceCollection();
        Services.AddConstructionKit();
        Services.AddDocumentationService();
        Services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.SetMinimumLevel(LogLevel.Trace);
        });
    }

    public ServiceCollection Services { get; }
}