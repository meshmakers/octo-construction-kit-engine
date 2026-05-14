using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.Configuration.DependencyInjection;
using Meshmakers.Octo.ConstructionKit.Engine.Documentation;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.ModelRepositories;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.Catalog;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.Repository;
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
        services.AddOptions<LocalFileSystemCatalogOptions>();
        services.AddOptions<PublicGitHubCatalogOptions>();
        services.AddOptions<PrivateGitHubCatalogOptions>();

        services.AddTransient<IHttpClientFactory, HttpClientFactory>();
        services.AddTransient<IGitHubClientFactory, GitHubClientFactory>();

        // Adding resolvers
        services.AddTransient<ICatalogDependencyResolver, CatalogDependencyResolver>();
        services.AddTransient<IRepositoryDependencyResolver, RepositoryDependencyResolver>();
        services.AddTransient<ICatalogModelResolver, CatalogModelResolver>();
        services.AddTransient<IRepositoryModelResolver, RepositoryModelResolver>();

        services.AddTransient<IElementResolver, ElementResolver>();
        services.AddTransient<IReferenceResolver, ReferenceResolver>();
        services.AddTransient<IInheritanceResolver, InheritanceResolver>();
        services.AddTransient<IVariableResolver, VariableResolver>();

        // Adding serializers
        services.AddTransient<ICkSerializer, CkYamlSerializer>();
        services.AddTransient<ICkYamlSerializer, CkYamlSerializer>();
        services.AddTransient<ICkJsonSerializer, CkJsonSerializer>();
        services.AddTransient<ICkSchemaValidator, CkSchemaValidator>();

        // Model stuff
        services.AddSingleton<ICatalogManager, CatalogManager>();
        services.AddTransient<Lazy<ICatalogManager>>(sp =>
            new Lazy<ICatalogManager>(sp.GetRequiredService<ICatalogManager>));
        services.AddSingleton<IRepositoryManagementService, RepositoryManagementService>();
        services.AddTransient<Lazy<IRepositoryManagementService>>(sp =>
            new Lazy<IRepositoryManagementService>(sp.GetRequiredService<IRepositoryManagementService>));

        // Adding services
        services.AddTransient<ICkMigrationParser, CkMigrationParser>();
        services.AddTransient<ICompilerService, CompilerService>();
        services.AddSingleton<ICkCacheService, CkCacheService>();
        services.AddTransient<ICkClassMappingService, CkClassMappingService>();
        services.AddTransient<ICatalogService, CatalogService>();

        // Add here sources of Ck model repositories
        services.AddTransient<ICatalog, LocalFileSystemCatalog>();
        services.AddTransient<ICatalog, EmbeddedResourceCatalog>();
        services.AddTransient<ICatalog, PrivateGitHubCatalog>();
        services.AddTransient<ICatalog, PublicGitHubCatalog>();

        // Blueprint catalog services
        services.AddOptions<LocalFileSystemBlueprintCatalogOptions>();
        services.AddOptions<PublicGitHubBlueprintCatalogOptions>();
        services.AddOptions<PrivateGitHubBlueprintCatalogOptions>();
        services.AddSingleton<IBlueprintCatalogManager, BlueprintCatalogManager>();
        services.AddTransient<IBlueprintCatalog, LocalFileSystemBlueprintCatalog>();
        services.AddTransient<IBlueprintCatalog, PublicGitHubBlueprintCatalog>();
        services.AddTransient<IBlueprintCatalog, PrivateGitHubBlueprintCatalog>();
        services.AddTransient<IBlueprintSerializer, BlueprintYamlSerializer>();
        services.AddTransient<IBlueprintSchemaValidator, BlueprintSchemaValidator>();
        services.AddTransient<IBlueprintCompilerService, BlueprintCompilerService>();

        return services;
    }

    /// <summary>
    ///     Adds Ck model compiler services to the DI container.
    /// </summary>
    /// <param name="services"></param>
    /// <returns></returns>
    public static IDocumentationBuilder AddDocumentationService(
        this IServiceCollection services)
    {
        services.AddOptions<ModeSelectionOptions>();
        
        //Helpers
        services.AddTransient<IDirectoryTools, DirectoryTools>();
        services.AddTransient<ILinkHelpers, LinkHelpers>();
        
        //Generators
        services.AddTransient<IMermaidGenerator, MermaidGenerator>();
        services.AddTransient<IContentGenerator, ContentGenerator>();

        return new DocumentationBuilder(services);
    }
}