using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Configuration;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.Configuration;
using Meshmakers.Octo.ConstructionKit.Engine.Documentation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NLog;
using NLog.Extensions.Logging;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Meshmakers.Octo.ConstructionKit.Compiler;

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
                    $".{Constants.OctoToolUserFolderName}{Path.DirectorySeparatorChar}settings.json"),
                true, true)
            .Build();

        services.Configure<OctoToolOptions>(options =>
            config.GetSection(Constants.OctoToolOptionsRootNode).Bind(options));
        services.Configure<LocalFileSystemCatalogOptions>(options =>
            config.GetSection(Constants.OctoToolLocalFileSystemCatalogRootNode).Bind(options));
        services.Configure<PublicGitHubCatalogOptions>(options =>
            config.GetSection(Constants.OctoToolPublicGitHubRootNode).Bind(options));
        services.Configure<PrivateGitHubCatalogOptions>(options =>
            config.GetSection(Constants.OctoToolPrivateGitHubRootNode).Bind(options));

        // Add Options for Running in ASP Net
        services.Configure<ModeSelectionOptions>(options => 
            config.GetSection(ModeSelectionOptions.ModeSelection).Bind(options));

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
            configWriter.AddOptions(Constants.OctoToolOptionsRootNode,
                provider.GetRequiredService<IOptions<OctoToolOptions>>());
            configWriter.AddOptions(Constants.OctoToolLocalFileSystemCatalogRootNode,
                provider.GetRequiredService<IOptions<LocalFileSystemCatalogOptions>>());
            configWriter.AddOptions(Constants.OctoToolPublicGitHubRootNode,
                provider.GetRequiredService<IOptions<PublicGitHubCatalogOptions>>());
            configWriter.AddOptions(Constants.OctoToolPrivateGitHubRootNode,
                provider.GetRequiredService<IOptions<PrivateGitHubCatalogOptions>>());
            return configWriter;
        });

        services.AddTransient<ICommand, ConfigCommand>();
        services.AddTransient<ICommand, NewCommand>();
        services.AddTransient<ICommand, CompileCommand>();
        services.AddTransient<ICommand, GetCommand>();
        services.AddTransient<ICommand, GetCatalogsCommand>();
        services.AddTransient<ICommand, PublishCommand>();
        services.AddTransient<ICommand, FindCommand>();
        services.AddTransient<ICommand, GenerateDocsCommand>();
        services.AddTransient<ICommand, RestoreCommand>();
        services.AddTransient<ICommand, VersionCommand>();
        services.AddTransient<ICommand, RefreshCommand>();
        services.AddTransient<ICommand, RefreshAllCommand>();

        services.AddDocumentationService();
        
        
        var serviceProvider = services.BuildServiceProvider();
        return serviceProvider;
    }
}