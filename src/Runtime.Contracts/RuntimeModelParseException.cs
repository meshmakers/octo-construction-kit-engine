using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
///     Represents an error that occurs during runtime model parsing.
/// </summary>
public class RuntimeModelParseException : PersistenceException
{
    /// <inheritdoc />
    private RuntimeModelParseException(string message) : base(message)
    {
        OperationResult = new OperationResult();
    }
    
    /// <inheritdoc />
    private RuntimeModelParseException(string message, Exception inner) : base(message, inner)
    {
        OperationResult = new OperationResult();
    }


    /// <inheritdoc />
    private RuntimeModelParseException(string message, OperationResult operationResult) : base(message)
    {
        OperationResult = operationResult;
    }

    /// <summary>
    ///     The <see cref="OperationResult" /> that caused the exception
    /// </summary>
    public OperationResult OperationResult { get; }

    internal static Exception CannotDeserializeModel(OperationResult operationResult)
    {
        return new RuntimeModelParseException("Stream contains invalid runtime model.", operationResult);
    }

    internal static Exception SchemaValidationFailed(string locationReference, OperationResult operationResult)
    {
        var details = operationResult.GetMessages();
        var message = string.IsNullOrWhiteSpace(details)
            ? $"{locationReference}: Stream contains invalid runtime model so that the schema validation failed."
            : $"{locationReference}: Stream contains invalid runtime model so that the schema validation failed. Details: {details.Trim()}";
        return new RuntimeModelParseException(message, operationResult);
    }

    internal static Exception InvalidStructure()
    {
        return new RuntimeModelParseException("Missing structure of JSON file format. Ensure that file begins with { \"entities\" : [ {");
    }

    internal static Exception CannotDeserializeEntity(int readerLineNumber)
    {
        return new RuntimeModelParseException($"Cannot deserialize entity at line {readerLineNumber}.");
    }

    internal static Exception InvalidPosition()
    {
        return new RuntimeModelParseException("Invalid position, the stream is not positioned on the 'entities' array.");
    }

    internal static Exception DuplicateEntity(OctoObjectId rtId)
    {
        return new RuntimeModelParseException($"Duplicate entity with RtId {rtId}.");
    }

    internal static Exception NotImplemented()
    {
        return new RuntimeModelParseException("Not implemented.");
    }

    internal static Exception KeyExpectedDuringDeserialization(string name)
    {
        return new RuntimeModelParseException($"'{name}' during deserialization.");
    }

    internal static Exception UnexpectedToken(string positionName, JsonTokenType readerTokenType, string numberName)
    {
        return new RuntimeModelParseException(
            $"Unexpected token parsing '{positionName}'. Expected '{numberName}', got '{(object)readerTokenType}'.");
    }

    internal static Exception UnexpectedEndOfStream(string positionName)
    {
        return new RuntimeModelParseException($"Unexpected end of stream parsing '{positionName}'.");
    }

    internal static Exception UnexpectedFormat(string typeName, Exception e)
    {
        return new RuntimeModelParseException($"Unexpected format for '{typeName}'.", e);
    }
    
    internal static Exception UnexpectedFormat(string propertyName)
    {
        return new RuntimeModelParseException($"Unexpected property '{propertyName}'.");
    }

    
    internal static Exception InvalidType(Type expectedType, object? value)
    {
        return new RuntimeModelParseException(
            $"Invalid type during serialization. Expected '{expectedType.Name}', got '{value?.GetType().Name}'.");
    }
    
    internal static Exception InvalidType(string? type)
    {
        return new RuntimeModelParseException($"CRS type {type} is unexpected.");
    }

    internal static Exception MissingProperty(string objectTypeName, string propertyName)
    {
        return new RuntimeModelParseException($"Serialized information of type '{objectTypeName}' must have a '{propertyName}' property.");
    }

    internal static Exception InvalidEnumValue<T>(string valueScalarValue)
    {
        return new RuntimeModelParseException($"Invalid enum value '{valueScalarValue}' for '{typeof(T).Name}'.");
    }

    internal static Exception InvalidExpectedEnumValue<T>(T result, T expectedValue)
    {
        return new RuntimeModelParseException($"Invalid expected enum value '{result}' for '{typeof(T).Name}'. Expected '{expectedValue}'.");
    }
}