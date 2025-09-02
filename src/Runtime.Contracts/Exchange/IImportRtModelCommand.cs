using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;

namespace Meshmakers.Octo.Runtime.Contracts.Exchange;

/// <summary>
///     Interface for importing a runtime model from a file.
/// </summary>
public interface IImportRtModelCommand
{
    /// <summary>
    ///     Imports as text
    /// </summary>
    /// <param name="runtimeRepository">The runtime repository</param>
    /// <param name="jsonText">Model as JSON text</param>
    /// <param name="importStrategy">Defines the import strategy</param>
    /// <param name="cancellationToken">An optional cancellation token</param>
    /// <returns></returns>
    Task ImportTextAsync(IRuntimeRepository runtimeRepository, string jsonText, ImportStrategy importStrategy,
        CancellationToken? cancellationToken = null);

    /// <summary>
    ///     Imports a model root
    /// </summary>
    /// <param name="runtimeRepository">The runtime repository</param>
    /// <param name="rtModelRootTc">The model root</param>
    /// <param name="importStrategy">Defines the import strategy</param>
    /// <param name="cancellationToken">An optional cancellation token</param>
    /// <returns></returns>
    Task ImportModelAsync(IRuntimeRepository runtimeRepository, RtModelRootTcDto rtModelRootTc,
        ImportStrategy importStrategy, CancellationToken? cancellationToken = null);

    /// <summary>
    ///     Imports from a file
    /// </summary>
    /// <param name="runtimeRepository">The runtime repository</param>
    /// <param name="filePath">A file path as ZIP (containing YAML or JSON), JSON or YAML files</param>
    /// <param name="contentType">The content type of the file</param>
    /// <param name="importStrategy">Defines the import strategy</param>
    /// <param name="cancellationToken">An optional cancellation token</param>
    /// <returns></returns>
    Task ImportAsync(IRuntimeRepository runtimeRepository, string filePath, string contentType,
        ImportStrategy importStrategy, CancellationToken? cancellationToken = null);
}