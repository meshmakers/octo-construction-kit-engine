using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.Runtime.Engine.Configuration.DependencyInjection;

/// <summary>
///     Octo builder Interface
/// </summary>
public interface IRuntimeEngineBuilder
{
    /// <summary>
    ///     Gets the services.
    /// </summary>
    /// <value>
    ///     The services.
    /// </value>
    IServiceCollection Services { get; }


    /// <summary>
    /// Adds a local runtime repository to the DI container
    /// </summary>
    /// <param name="setupRepositoryConfigurationAction"></param>
    public IRuntimeEngineBuilder AddLocalRuntimeRepository(
        Action<LocalRuntimeRepositoryConfiguration>? setupRepositoryConfigurationAction = null);
}
