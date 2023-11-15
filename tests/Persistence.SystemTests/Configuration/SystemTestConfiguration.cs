using Microsoft.Extensions.Configuration;

namespace Meshmakers.Octo.SystematizedData.Persistence.SystemTests.Configuration;

public class SystemTestConfiguration
{
    private readonly IConfiguration _configuration;

    public SystemTestConfiguration()
    {
        _configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.test.json", true)
            .Build();
    }

    public IConfigurationSection GetSection(string section)
    {
        return _configuration.GetSection(section);
    }
}
