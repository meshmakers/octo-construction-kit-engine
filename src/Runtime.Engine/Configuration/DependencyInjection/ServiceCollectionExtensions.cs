
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Engine.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Serialization;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for adding Ck model compiler services to the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Ck model compiler services to the DI container.
    /// </summary>
    /// <param name="services"></param>
    /// <returns></returns>
    public static IServiceCollection AddRuntimeEngine(
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

        return services;
    }
}