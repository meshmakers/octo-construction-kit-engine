using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Services;

/// <summary>
///     Validates a compiled model. This validation validates logical consistency of the model.
/// </summary>
public interface ICkValidationService
{
    /// <summary>
    ///     Checks the logical consistency of the compiled model.
    /// </summary>
    /// <param name="compiledModel">Compiled construction kit model</param>
    /// <param name="originFileResolver">Resolver for the original file location</param>
    /// <param name="operationResult">The result object that contains after call validation messages.</param>
    /// <returns></returns>
    Task<CkModelGraph> ValidateAsync(CkCompiledModelRoot compiledModel, IOriginFileResolver originFileResolver, OperationResult operationResult);
    
    /// <summary>
    ///     Checks the logical consistency of the compiled model.
    /// </summary>
    /// <param name="compiledModel">Compiled construction kit model</param>
    /// <param name="operationResult">The result object that contains after call validation messages.</param>
    /// <returns></returns>
    Task<CkModelGraph> ValidateAsync(CkCompiledModelRoot compiledModel, OperationResult operationResult);
}