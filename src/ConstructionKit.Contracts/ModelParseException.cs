using System.Text.Json;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

public class ModelParseException : CkModelException
{
    public ModelParseException()
    {
    }

    public ModelParseException(string message) : base(message)
    {
    }

    public ModelParseException(string message, Exception inner) : base(message, inner)
    {
    }

    internal static Exception UnexpectedToken(string elementName, JsonTokenType readerTokenType)
    {
        return new ModelParseException($"Unexpected token parsing '{elementName}'. Expected String, got '{(object)readerTokenType}'.");
    }

    public static Exception ValueCannotBeEmpty(string elementName)
    {
        return new ModelParseException($"Value cannot be null or empty for element '{elementName}'.");
    }

    public static Exception CannotDeserializeModel(string filePath)
    {
        return new ModelParseException($"File '{filePath}' contains invalid construction kit model.");
    }
    
    public static Exception CannotDeserializeModel()
    {
        return new ModelParseException($"Stream contains invalid construction kit model.");
    }
    
    public static Exception SchemaValidationFailed()
    {
        return new ModelParseException($"Stream contains invalid construction kit model so that the schema validation failed.");
    }

    public static Exception CannotDeserializeRtModel(string filePath)
    {
        return new ModelParseException($"File '{filePath}' contains invalid runtime model.");
    }
    
    public static Exception CannotDeserializeModeByJsonString(string jsonString)
    {
        return new ModelParseException($"JSON string '{jsonString}' contains invalid construction kit model.");
    }

    public static Exception CommonErrorReadCkModel(string filePath, Exception exception)
    {
        return new ModelParseException($"File '{filePath}' cannot be read.", exception);
    }
    
    public static Exception CommonErrorReadRtModel(Exception exception)
    {
        return new ModelParseException($"Cannot be read runtime model.", exception);
    }
}