using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Configuration;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.BlueprintManager.Commands.Implementations;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NLog;
using NLog.Extensions.Logging;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Meshmakers.Octo.BlueprintManager;

internal static class Program
{
    private static async Task<int> Main()
    {
        var logger = LogManager.GetCurrentClassLogger();
        try
        {
            var servicesProvider = BuildDi();
            using (servicesProvider as IDisposable)
            {
                var runner = servicesProvider.GetRequiredService<Runner>();
                return await runner.DoActionAsync();
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Stopped program because of exception");
            return -100;
        }
        finally
        {
            // Ensure to flush and stop internal timers/threads before application-exit (Avoid segmentation fault on Linux)
            LogManager.Shutdown();
        }
    }

    private static IServiceProvider BuildDi()
    {
        var services = new ServiceCollection();

        // AddRuntimeEngine() internally calls AddConstructionKit()
        services.AddRuntimeEngine();

        // Runner is the custom class
        services.AddTransient<Runner>();

        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", true, true)
            .AddJsonFile(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    $".{Constants.BpmToolUserFolderName}{Path.DirectorySeparatorChar}settings.json"),
                true, true)
            .Build();

        services.Configure<BpmToolOptions>(options =>
            config.GetSection(Constants.BpmToolOptionsRootNode).Bind(options));
        services.Configure<LocalFileSystemBlueprintCatalogOptions>(options =>
            config.GetSection(Constants.BpmToolLocalFileSystemCatalogRootNode).Bind(options));
        services.Configure<PublicGitHubBlueprintCatalogOptions>(options =>
            config.GetSection(Constants.BpmToolPublicGitHubRootNode).Bind(options));
        services.Configure<PrivateGitHubBlueprintCatalogOptions>(options =>
            config.GetSection(Constants.BpmToolPrivateGitHubRootNode).Bind(options));

        // configure Logging with NLog
        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.SetMinimumLevel(LogLevel.Trace);
            loggingBuilder.AddNLog(config);
        });

        services.AddSingleton<IConsoleService, ConsoleService>();
        services.AddSingleton<IEnvironmentService, EnvironmentService>();
        services.AddSingleton<IParserService, ParserService>();
        services.AddSingleton<ICommandParser, CommandParser>();
        services.AddSingleton<IConfigWriter, ConfigWriter>(provider =>
        {
            var configWriter = new ConfigWriter();
            configWriter.AddOptions(Constants.BpmToolOptionsRootNode,
                provider.GetRequiredService<IOptions<BpmToolOptions>>());
            configWriter.AddOptions(Constants.BpmToolLocalFileSystemCatalogRootNode,
                provider.GetRequiredService<IOptions<LocalFileSystemBlueprintCatalogOptions>>());
            configWriter.AddOptions(Constants.BpmToolPublicGitHubRootNode,
                provider.GetRequiredService<IOptions<PublicGitHubBlueprintCatalogOptions>>());
            configWriter.AddOptions(Constants.BpmToolPrivateGitHubRootNode,
                provider.GetRequiredService<IOptions<PrivateGitHubBlueprintCatalogOptions>>());
            return configWriter;
        });

        // Configuration command
        services.AddTransient<ICommand, ConfigCommand>();

        // Blueprint commands (without "blueprint-" prefix)
        services.AddTransient<ICommand, NewCommand>();
        services.AddTransient<ICommand, ValidateCommand>();
        services.AddTransient<ICommand, PackCommand>();
        services.AddTransient<ICommand, ListCommand>();
        services.AddTransient<ICommand, StatusCommand>();
        services.AddTransient<ICommand, PreviewCommand>();
        services.AddTransient<ICommand, UpdateCommand>();
        services.AddTransient<ICommand, HistoryCommand>();
        services.AddTransient<ICommand, VersionCommand>();

        // Catalog commands
        services.AddTransient<ICommand, CatalogsCommand>();
        services.AddTransient<ICommand, GetCommand>();
        services.AddTransient<ICommand, PublishCommand>();

        var serviceProvider = services.BuildServiceProvider();
        return serviceProvider;
    }
}
