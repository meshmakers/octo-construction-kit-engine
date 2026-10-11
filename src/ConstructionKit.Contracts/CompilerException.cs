using Meshmakers.Octo.ConstructionKit.Contracts.Messages;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Used to indicate an exception during compiler operations
/// </summary>
public class CompilerException : Exception
{
    /// <summary>
    ///     Creates a new instance of <see cref="CompilerException" />
    /// </summary>
    /// <param name="operationResult"></param>
    public CompilerException(OperationResult operationResult)
        : base("Compiler result contains errors")
    {
        OperationResult = operationResult;
    }

    /// <summary>
    ///     Creates a new instance of <see cref="CompilerException" />
    /// </summary>
    /// <param name="message"></param>
    /// <param name="operationResult"></param>
    public CompilerException(string message, OperationResult operationResult) : base(message)
    {
        OperationResult = operationResult;
    }

    /// <summary>
    ///     Creates a new instance of <see cref="CompilerException" />
    /// </summary>
    /// <param name="message"></param>
    /// <param name="inner"></param>
    /// <param name="operationResult"></param>
    public CompilerException(string message, Exception inner, OperationResult operationResult) : base(message, inner)
    {
        OperationResult = operationResult;
    }

    /// <summary>
    ///     The <see cref="OperationResult" /> that caused the exception
    /// </summary>
    public OperationResult OperationResult { get; }

    internal static Exception OperationResultWithErrors(OperationResult operationResult)
    {
        return new CompilerException(operationResult);
    }

    /// <summary>
    ///     The model could not be resolved, so it was NOT written to the catalog. Carries the resolve
    ///     messages both in the exception text (so a caller that only logs <see cref="Exception.Message" />
    ///     stays actionable) and in <see cref="OperationResult" />.
    /// </summary>
    internal static Exception PublishFailedWithErrors(string catalogName, CkModelId ckModelId,
        IEnumerable<OperationMessage> resolveMessages, OperationResult operationResult)
    {
        var errors = string.Join("; ", resolveMessages
            .Where(m => m.MessageLevel is MessageLevel.Error or MessageLevel.FatalError)
            .Select(m => $"{m.MessageNumber} {m.Location}: {m.MessageText}"));
        return new CompilerException(
            $"Construction kit model '{ckModelId.FullName}' was NOT published to catalog '{catalogName}' " +
            $"because it could not be resolved: {errors}", operationResult);
    }

    internal static Exception DirectoryMustBeEmpty(string rootPath, OperationResult operationResult)
    {
        return new CompilerException($"Directory '{rootPath}' must be empty", operationResult);
    }

    internal static Exception DirectoryDoesNotExist(string rootPath, OperationResult operationResult)
    {
        return new CompilerException($"Directory '{rootPath}' does not exist", operationResult);
    }

    internal static Exception FileDoesNotExist(string modelPath, OperationResult operationResult)
    {
        return new CompilerException($"File '{modelPath}' does not exist", operationResult);
    }

    internal static Exception ModelParseFailed(string path, ModelParseException modelParseException, OperationResult operationResult)
    {
        return new CompilerException($"Model parse failed for '{path}': {modelParseException.Message}",
            modelParseException, operationResult);
    }
}