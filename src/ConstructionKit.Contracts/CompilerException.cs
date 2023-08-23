namespace Meshmakers.Octo.ConstructionKit.Contracts;

public class CompilerException : Exception
{
    public CompilerException(OperationResult operationResult) 
        : base("Compiler result contains errors")
    {
        OperationResult = operationResult;
    }

    public CompilerException(string message, OperationResult operationResult) : base(message)
    {
        OperationResult = operationResult;
    }

    public CompilerException(string message, Exception inner, OperationResult operationResult) : base(message, inner)
    {
        OperationResult = operationResult;
    }

    public OperationResult OperationResult { get; }

    public static Exception OperationResultWithErrors(OperationResult operationResult)
    {
        return new CompilerException(operationResult);
    }
    
    public static Exception DirectoryMustBeEmpty(string rootPath, OperationResult operationResult)
    {
        return new CompilerException($"Directory '{rootPath}' must be empty", operationResult);
    }

    public static Exception DirectoryDoesNotExist(string rootPath, OperationResult operationResult)
    {
        return new CompilerException($"Directory '{rootPath}' does not exist", operationResult);
    }

    public static Exception FileDoesNotExist(string modelPath, OperationResult operationResult)
    {
        return new CompilerException($"File '{modelPath}' does not exist", operationResult);
    }
}
