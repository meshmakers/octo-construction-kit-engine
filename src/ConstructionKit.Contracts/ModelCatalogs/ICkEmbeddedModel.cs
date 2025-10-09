using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;

/// <summary>
///     Interface of an embedded construction kit model.
/// </summary>
public interface ICkEmbeddedModel
{
    /// <summary>
    ///     Returns the model id of the embedded model.
    /// </summary>
    CkModelId ModelId { get; }

    /// <summary>
    /// Returns a short description of the model.
    /// </summary>
    string Description { get; }

    /// <summary>
    ///     Returns the deserialized model.
    /// </summary>
    /// <returns></returns>
    Task<CkCompiledModelRoot> GetCompiledModelRootAsync(OperationResult operationResult);
}