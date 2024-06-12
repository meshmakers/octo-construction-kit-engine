using Meshmakers.Octo.ConstructionKit.Engine.Documentation;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.ConstructionKit.Engine.Configuration.DependencyInjection;

/// <summary>
/// 
/// </summary>
public class DocumentationBuilder : IDocumentationBuilder
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="DocumentationBuilder" /> class.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <exception cref="System.ArgumentNullException">services</exception>
    internal DocumentationBuilder(IServiceCollection services)
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
    /// 
    /// </summary>
    /// <param name="modeSelection"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public IDocumentationBuilder ChangeDocumentationMode(Action<ModeSelectionOptions>? modeSelection = null)
    {
        if (modeSelection != null)
        {
            Services.Configure(modeSelection);
        }

        return this;
    }
}