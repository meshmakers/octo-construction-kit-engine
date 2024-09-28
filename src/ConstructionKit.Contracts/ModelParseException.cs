using System.Text.Json;
using Newtonsoft.Json;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Used to indicate an exception during model parsing operations
/// </summary>
public class ModelParseException : CkModelException
{
    /// <inheritdoc />
    private ModelParseException(string message) : base(message)
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
    ///     The <see cref="OperationResult" /> that caused the exception
    /// </summary>
    public OperationResult OperationResult { get; }

    internal static Exception UnexpectedToken(string elementName, JsonTokenType readerTokenType, string expectedString)
    {
        return new ModelParseException(
            $"Unexpected token parsing '{elementName}'. Expected '{expectedString}', got '{(object)readerTokenType}'.");
    }

    internal static Exception UnexpectedToken(string elementName, JsonToken readerTokenType, string expectedString)
    {
        return new ModelParseException(
            $"Unexpected token parsing '{elementName}'. Expected '{expectedString}', got '{(object)readerTokenType}'.");
    }

    internal static Exception ValueCannotBeEmpty(string elementName)
    {
        return new ModelParseException($"Value cannot be null or empty for element '{elementName}'.");
    }

    internal static Exception CannotDeserializeModel(string locationReference, OperationResult operationResult, Exception e)
    {
        return new ModelParseException($"Location '{locationReference}' contains invalid construction kit model.", e, operationResult);
    }
    
    internal static Exception DeserializedModelWasNull(string locationReference, OperationResult operationResult)
    {
        return new ModelParseException($"Location '{locationReference}' contains invalid construction kit model. The deserialized value was null.", operationResult);
    }

    internal static Exception SchemaValidationFailed(string locationReference, OperationResult operationResult)
    {
        return new ModelParseException(
            $"{locationReference}: Stream contains invalid construction kit model so that the schema validation failed.",
            operationResult);
    }
}