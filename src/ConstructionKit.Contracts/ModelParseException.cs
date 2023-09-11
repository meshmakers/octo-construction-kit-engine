using System.Text.Json;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Used to indicate an exception during model parsing operations
/// </summary>
public class ModelParseException : CkModelException
{
    /// <inheritdoc />
    public ModelParseException()
    {
    }

    /// <inheritdoc />
    public ModelParseException(string message) : base(message)
    {
    }

    /// <inheritdoc />
    public ModelParseException(string message, Exception inner) : base(message, inner)
    {
    }

    internal static Exception UnexpectedToken(string elementName, JsonTokenType readerTokenType, string expectedString)
    {
        return new ModelParseException($"Unexpected token parsing '{elementName}'. Expected '{expectedString}', got '{(object)readerTokenType}'.");
    }
    
    internal static Exception ValueCannotBeEmpty(string elementName)
    {
        return new ModelParseException($"Value cannot be null or empty for element '{elementName}'.");
    }

    internal static Exception CannotDeserializeModel(string filePath)
    {
        return new ModelParseException($"File '{filePath}' contains invalid construction kit model.");
    }
    
    internal static Exception CannotDeserializeModel()
    {
        return new ModelParseException($"Stream contains invalid construction kit model.");
    }
    
    internal static Exception SchemaValidationFailed()
    {
        return new ModelParseException($"Stream contains invalid construction kit model so that the schema validation failed.");
    }

    internal static Exception CannotDeserializeRtModel(string filePath)
    {
        return new ModelParseException($"File '{filePath}' contains invalid runtime model.");
    }
    
    internal static Exception CannotDeserializeModeByJsonString(string jsonString)
    {
        return new ModelParseException($"JSON string '{jsonString}' contains invalid construction kit model.");
    }

    internal static Exception CommonErrorReadCkModel(string filePath, Exception exception)
    {
        return new ModelParseException($"File '{filePath}' cannot be read.", exception);
    }
    
    internal static Exception CommonErrorReadRtModel(Exception exception)
    {
        return new ModelParseException($"Cannot be read runtime model.", exception);
    }
}