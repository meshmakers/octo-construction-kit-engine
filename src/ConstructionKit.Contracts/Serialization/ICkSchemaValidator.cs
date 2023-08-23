namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

public interface ICkSchemaValidator
{
    bool ValidateElementsInJson(Stream stream, OperationResult operationResult);
    bool ValidateMetaInJson(Stream stream, OperationResult operationResult);
    bool ValidateCompiledModelInJson(Stream stream, OperationResult operationResult);

    bool ValidateElementsInYaml(Stream stream, OperationResult operationResult);
    bool ValidateMetaInYaml(Stream stream, OperationResult operationResult);
    bool ValidateCompiledModelInYaml(Stream stream, OperationResult operationResult);
}