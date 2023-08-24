using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;
using Meshmakers.Octo.ConstructionKit.Contracts.Resolvers;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Contracts.Validation;
using Meshmakers.Octo.ConstructionKit.Engine.ModelRepositories;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;
using Meshmakers.Octo.ConstructionKit.Engine.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Validation;

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
    public static IServiceCollection AddCkModelCompiler(
        this IServiceCollection services)
    {
        // Adding resolvers
        services.AddTransient<IDependencyResolver, DependencyResolver>();
        services.AddTransient<IElementResolver, ElementResolver>();
        services.AddTransient<IInheritanceResolver, InheritanceResolver>();
        
        // Adding serializers
        services.AddTransient<ICkSerializer, CkYamlSerializer>();
        services.AddTransient<ICkYamlSerializer, CkYamlSerializer>();
        services.AddTransient<ICkJsonSerializer, CkJsonSerializer>();
        services.AddTransient<ICkSchemaValidator, CkSchemaValidator>();
        
        // Model stuff
        services.AddTransient<ICkModelValidator, CkModelValidator>();
        services.AddSingleton<ICkModelRepositoryManager, CkModelRepositoryManager>();
        
        // Adding services
        services.AddSingleton<ICompilerService, CompilerService>();
        
        
        // Add here sources of Ck model repositories
        services.AddTransient<ICkModelRepository, LocalFileSystemCkModelRepository>();

        return services;
    }
}