using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Engine.Repositories.Local;
using Meshmakers.Octo.Runtime.Engine.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.Runtime.Engine.Configuration.DependencyInjection;

/// <summary>
///     IdentityServer helper class for DI configuration
/// </summary>
public class RuntimeEngineBuilder : IRuntimeEngineBuilder
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="RuntimeEngineBuilder" /> class.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <exception cref="System.ArgumentNullException">services</exception>
    internal RuntimeEngineBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    ///     Gets the services.
    /// </summary>
    /// <value>
    ///     The services.
    /// </value>
    public IServiceCollection Services { get; }


    /// <summary>
    ///     Adds a local runtime repository to the DI container
    /// </summary>
    /// <param name="setupRepositoryConfigurationAction"></param>
    public IRuntimeEngineBuilder AddLocalRuntimeRepository(
        Action<LocalRuntimeRepositoryConfiguration>? setupRepositoryConfigurationAction = null)
    {
        if (setupRepositoryConfigurationAction != null)
        {
            Services.Configure(setupRepositoryConfigurationAction);
        }

        Services.AddTransient<IRtRepositorySerializer, RtRepositorySerializer>();

        return this;
    }
}