using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.ModelRepositories;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;
using Meshmakers.Octo.ConstructionKit.Engine.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.Services;

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
    public static IServiceCollection AddConstructionKit(
        this IServiceCollection services)
    {
        // Adding resolvers
        services.AddTransient<IDependencyResolver, DependencyResolver>();
        services.AddTransient<IElementResolver, ElementResolver>();
        services.AddTransient<IReferenceResolver, ReferenceResolver>();
        services.AddTransient<IInheritanceResolver, InheritanceResolver>();
        services.AddTransient<IModelResolver, ModelResolver>();

        // Adding serializers
        services.AddTransient<ICkSerializer, CkYamlSerializer>();
        services.AddTransient<ICkYamlSerializer, CkYamlSerializer>();
        services.AddTransient<ICkJsonSerializer, CkJsonSerializer>();
        services.AddTransient<ICkSchemaValidator, CkSchemaValidator>();

        // Model stuff
        services.AddSingleton<ICkModelRepositoryManager, CkModelRepositoryManager>();
        services.AddTransient<Lazy<ICkModelRepositoryManager>>(sp =>
            new Lazy<ICkModelRepositoryManager>(sp.GetRequiredService<ICkModelRepositoryManager>));
        // Adding services
        services.AddTransient<ICompilerService, CompilerService>();
        services.AddTransient<ICkValidationService, CkValidationService>();
        services.AddSingleton<ICkCacheService, CkCacheService>();
        services.AddTransient<ICkModelRepositoryService, CkModelRepositoryService>();

        // Add here sources of Ck model repositories
        services.AddTransient<ICkModelRepository, LocalFileSystemCkModelRepository>();
        services.AddTransient<ICkModelRepository, EmbeddedResourceCkModelRepository>();

        return services;
    }
}