using Meshmakers.Octo.ConstructionKit.Engine.Documentation;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.ConstructionKit.Engine.Configuration.DependencyInjection;

/// <summary>
/// Allows for Selection of Documentation Mode
/// </summary>
public interface IDocumentationBuilder
{
    /// <summary>
    ///     Gets the services.
    /// </summary>
    /// <value>
    ///     The services.
    /// </value>
    IServiceCollection Services { get; }
    
    
    /// <summary>
    ///     Adds a local runtime repository to the DI container
    /// </summary>
    /// <param name="modeSelection"></param>
    public IDocumentationBuilder ChangeDocumentationMode(
        Action<ModeSelectionOptions>? modeSelection = null);
}