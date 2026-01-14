using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Meshmakers.Octo.Runtime.Contracts.Exchange;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer;
using Meshmakers.Octo.Runtime.Engine.Blueprints;
using Meshmakers.Octo.Runtime.Engine.Configuration.DependencyInjection;
using Meshmakers.Octo.Runtime.Engine.Exchange;
using Meshmakers.Octo.Runtime.Engine.Repositories;
using Meshmakers.Octo.Runtime.Engine.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Serialization;
using Meshmakers.Octo.Runtime.Engine.TransportContainer;
using Microsoft.Extensions.Logging;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extension methods for adding Ck model compiler services to the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Adds Ck model compiler services to the DI container.
    /// </summary>
    /// <param name="services"></param>
    /// <returns></returns>
    public static IRuntimeEngineBuilder AddRuntimeEngine(
        this IServiceCollection services)
    {
        // Runtime needs as base the construction kit
        services.AddConstructionKit();

        // Adding serializers
        services.AddTransient<IRtSerializer, RtYamlSerializer>();
        services.AddTransient<IRtYamlSerializer, RtYamlSerializer>();
        services.AddTransient<IRtJsonSerializer, RtJsonSerializer>();
        services.AddTransient<IRtSchemaValidator, RtSchemaValidator>();

        // Add rule engine
        services.AddTransient<IEntityRuleEngine, EntityRuleEngine>();
        services.AddTransient<IGraphRuleEngine, GraphRuleEngine>();

        // Implementation of bulk operations
        services.AddTransient<IBulkRtMutation, BulkRtMutation>();
        services.AddTransient<IImportRtModelCommand, ImportRtModelCommand>();

        // Add converters
        services.AddTransient<IRtEntityToTcDtoConverter, RtEntityToTcDtoConverter>();

        // Blueprint services
        services.AddSingleton<ITenantBlueprintHistory, InMemoryTenantBlueprintHistory>();
        services.AddSingleton<ITenantBackupService, InMemoryTenantBackupService>();
        services.AddTransient<IBlueprintService, BlueprintService>();
        services.AddTransient<IMigrationExecutor, MigrationExecutor>();
        services.AddTransient<IMigrationParser, MigrationParser>();

        // CK model migration services
        services.AddSingleton<IRuntimeRepositoryProvider, RuntimeRepositoryProvider>();
        services.AddTransient<ICkMigrationParser, CkMigrationParser>();

        // Migration content providers (aggregate by default, allows adding multiple sources)
        services.AddSingleton<FileSystemCkMigrationContentProvider>();
        services.AddSingleton<EmbeddedCkMigrationContentProvider>();
        services.AddSingleton<ICkMigrationContentProvider>(sp =>
        {
            var aggregate = new AggregateCkMigrationContentProvider(
                sp.GetRequiredService<ILogger<AggregateCkMigrationContentProvider>>());

            // Add embedded resources first (higher priority)
            aggregate.AddProvider(sp.GetRequiredService<EmbeddedCkMigrationContentProvider>());
            // Then file system as fallback
            aggregate.AddProvider(sp.GetRequiredService<FileSystemCkMigrationContentProvider>());

            return aggregate;
        });

        services.AddTransient<ICkModelMigrationService, CkModelMigrationService>();
        services.AddTransient<ICkModelUpgradeService, CkModelUpgradeService>();

        return new RuntimeEngineBuilder(services);
    }
}