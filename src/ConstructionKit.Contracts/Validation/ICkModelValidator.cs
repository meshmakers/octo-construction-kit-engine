using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Validation;

public interface ICkModelValidator
{
    Task ValidateAsync(CkCompiledModelRoot compiledModel, OperationResult operationResult);
}