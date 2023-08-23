using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CkModel.Compiler.SystemTests.Fixtures;

public class ServiceCollectionFixture
{
    public ServiceCollection Services { get; }

    public ServiceCollectionFixture()
    {
        Services = new ServiceCollection();
        Services.AddCkModelCompiler();
        Services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.SetMinimumLevel(LogLevel.Trace);
        });
    }

    

}