using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Validation;

/// <summary>
/// Validates a compiled model. This validation validates logical consistency of the model.
/// </summary>
public interface ICkModelValidator
{
    /// <summary>
    /// Checks the logical consistency of the compiled model.
    /// </summary>
    /// <param name="compiledModel">Compiled construction kit model</param>
    /// <param name="operationResult">The result object that contains after call validation messages.</param>
    /// <returns></returns>
    Task ValidateAsync(CkCompiledModelRoot compiledModel, OperationResult operationResult);
}