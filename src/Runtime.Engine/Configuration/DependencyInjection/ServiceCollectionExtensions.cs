using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Meshmakers.Octo.Runtime.Contracts.CkModelMigrations;
using Meshmakers.Octo.Runtime.Contracts.Exchange;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer;
using Meshmakers.Octo.Runtime.Engine.Blueprints;
using Meshmakers.Octo.Runtime.Engine.CkModelMigrations;
using Meshmakers.Octo.Runtime.Engine.Configuration.DependencyInjection;
using Meshmakers.Octo.Runtime.Engine.Exchange;
using Meshmakers.Octo.Runtime.Engine.Repositories;
using Meshmakers.Octo.Runtime.Engine.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Serialization;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Meshmakers.Octo.Runtime.Engine.TransportContainer;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        services.AddSingleton<ITenantBlueprintInstallations, InMemoryTenantBlueprintInstallations>();
        services.AddTransient<IBlueprintService, BlueprintService>();
        services.AddTransient<IBlueprintDependencyResolver, BlueprintDependencyResolver>();
        services.AddTransient<IBlueprintMigrationExecutor, BlueprintMigrationExecutor>();
        services.AddTransient<IBlueprintMigrationParser, BlueprintMigrationParser>();
        services.TryAddSingleton<IBlueprintNotifications, LoggingBlueprintNotifications>();

        // Blueprint variable resolution (overridable via TryAdd — services that need a richer
        // provider, e.g. one that surfaces chart names or feature flags, can register their own
        // IBlueprintVariableProvider before calling AddRuntimeEngine and win the registration.)
        services.AddOptions<OctoBlueprintVariablesOptions>();
        services.TryAddTransient<IBlueprintVariableProvider, DefaultBlueprintVariableProvider>();

        // CK model migration services
        services.AddSingleton<IRuntimeRepositoryProvider, RuntimeRepositoryProvider>();

        // Migration content providers (aggregate by default, allows adding multiple sources)
        services.AddSingleton<CompiledModelCkMigrationContentProvider>();
        services.AddSingleton<FileSystemCkMigrationContentProvider>();
        services.AddSingleton<EmbeddedCkMigrationContentProvider>();
        services.AddSingleton<ICkMigrationContentProvider>(sp =>
        {
            var aggregate = new AggregateCkMigrationContentProvider(
                sp.GetRequiredService<ILogger<AggregateCkMigrationContentProvider>>());

            // Add compiled model migrations first (highest priority, populated during import)
            aggregate.AddProvider(sp.GetRequiredService<CompiledModelCkMigrationContentProvider>());
            // Then embedded resources (NuGet package references)
            aggregate.AddProvider(sp.GetRequiredService<EmbeddedCkMigrationContentProvider>());
            // Then file system as fallback (local dev)
            aggregate.AddProvider(sp.GetRequiredService<FileSystemCkMigrationContentProvider>());

            return aggregate;
        });

        services.AddTransient<ICkModelMigrationService, CkModelMigrationService>();
        services.AddTransient<ICkModelUpgradeService, CkModelUpgradeService>();

        // StreamData archive lifecycle. Concept §3, §11. The lifecycle service itself is
        // constructed per-tenant by the host (e.g. Mongo TenantContext) because it requires a
        // tenant id; this registration only wires the audit-trail default. The default writes
        // structured log entries; a host can replace it by registering a different
        // IArchiveAuditTrail implementation (e.g. EventBusArchiveAuditTrail in
        // octo-common-services).
        services.AddTransient<IArchiveAuditTrail, LoggingArchiveAuditTrail>();

        return new RuntimeEngineBuilder(services);
    }
}