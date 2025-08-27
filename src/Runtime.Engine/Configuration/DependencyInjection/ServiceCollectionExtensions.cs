using Meshmakers.Octo.Runtime.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts.Exchange;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Engine.Configuration.DependencyInjection;
using Meshmakers.Octo.Runtime.Engine.DataTransferObjects;
using Meshmakers.Octo.Runtime.Engine.Exchange;
using Meshmakers.Octo.Runtime.Engine.Repositories;
using Meshmakers.Octo.Runtime.Engine.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Serialization;

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
        services.AddTransient<IRtEntityToDtoConverter, RtEntityToDtoConverter>();

        return new RuntimeEngineBuilder(services);
    }
}