using System.Text.Json;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Used to indicate an exception during model parsing operations
/// </summary>
public class ModelParseException : CkModelException
{
    /// <inheritdoc />
    private ModelParseException(string message): base(message)
    {
        OperationResult = new OperationResult();
    }
    
    /// <inheritdoc />
    private ModelParseException(string message, OperationResult operationResult) : base(message)
    {
        OperationResult = operationResult;
    }
    
    /// <inheritdoc />
    private ModelParseException(string message, Exception inner, OperationResult operationResult) : base(message, inner)
    {
        OperationResult = operationResult;
    }
    
    /// <summary>
    /// The <see cref="OperationResult"/> that caused the exception
    /// </summary>
    public OperationResult OperationResult { get; }

    internal static Exception UnexpectedToken(string elementName, JsonTokenType readerTokenType, string expectedString)
    {
        return new ModelParseException(
            $"Unexpected token parsing '{elementName}'. Expected '{expectedString}', got '{(object)readerTokenType}'.");
    }
    
    internal static Exception ValueCannotBeEmpty(string elementName)
    {
        return new ModelParseException($"Value cannot be null or empty for element '{elementName}'.");
    }

    internal static Exception CannotDeserializeModel(string filePath, OperationResult operationResult)
    {
        return new ModelParseException($"File '{filePath}' contains invalid construction kit model.", operationResult);
    }
    
    internal static Exception CannotDeserializeModel(OperationResult operationResult)
    {
        return new ModelParseException($"Stream contains invalid construction kit model.", operationResult);
    }
    
    internal static Exception SchemaValidationFailed(string locationReference, OperationResult operationResult)
    {
        return new ModelParseException($"{locationReference}: Stream contains invalid construction kit model so that the schema validation failed.", operationResult);
    }

    internal static Exception CannotDeserializeRtModel(string filePath, OperationResult operationResult)
    {
        return new ModelParseException($"File '{filePath}' contains invalid runtime model.", operationResult);
    }
    
    internal static Exception CannotDeserializeModeByJsonString(string jsonString, OperationResult operationResult)
    {
        return new ModelParseException($"JSON string '{jsonString}' contains invalid construction kit model.", operationResult);
    }

    internal static Exception CommonErrorReadCkModel(string filePath, Exception exception, OperationResult operationResult)
    {
        return new ModelParseException($"File '{filePath}' cannot be read.", exception, operationResult);
    }
    
    internal static Exception CommonErrorReadRtModel(Exception exception, OperationResult operationResult)
    {
        return new ModelParseException($"Cannot be read runtime model.", exception, operationResult);
    }
}