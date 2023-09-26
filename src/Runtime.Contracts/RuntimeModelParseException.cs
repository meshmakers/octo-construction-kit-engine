using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
/// Represents an error that occurs during runtime model parsing.
/// </summary>
public class RuntimeModelParseException : PersistenceException
{
    /// <inheritdoc />
    private RuntimeModelParseException(string message): base(message)
    {
        OperationResult = new OperationResult();
    }
    
    /// <inheritdoc />
    private RuntimeModelParseException(string message, OperationResult operationResult) : base(message)
    {
        OperationResult = operationResult;
    }
    
    /// <inheritdoc />
    private RuntimeModelParseException(string message, Exception inner, OperationResult operationResult) : base(message, inner)
    {
        OperationResult = operationResult;
    }
    
    /// <summary>
    /// The <see cref="OperationResult"/> that caused the exception
    /// </summary>
    public OperationResult OperationResult { get; }

    internal static Exception CannotDeserializeModel(OperationResult operationResult)
    {
        return new RuntimeModelParseException($"Stream contains invalid runtime model.", operationResult);
    }

    internal static Exception SchemaValidationFailed(string locationReference, OperationResult operationResult)
    {
        return new RuntimeModelParseException($"{locationReference}: Stream contains invalid runtime model so that the schema validation failed.", operationResult);
    }
}   

