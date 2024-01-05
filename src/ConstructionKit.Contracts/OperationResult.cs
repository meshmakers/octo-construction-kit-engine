using System.Text;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Represents the result of an construction kit operation
/// </summary>
public class OperationResult
{
    /// <summary>
    ///     Creates a new instance of <see cref="OperationResult" />
    /// </summary>
    public OperationResult()
    {
        Messages = new List<OperationMessage>();
    }

    /// <summary>
    ///     Returns the messages of the operation
    /// </summary>
    public List<OperationMessage> Messages { get; }

    /// <summary>
    ///     Returns true if the operation has errors
    /// </summary>
    public bool HasErrors => Messages.Any(x => x.MessageLevel == MessageLevel.Error);

    /// <summary>
    ///     Returns true if the operation has fatal errors
    /// </summary>
    public bool HasFatalErrors => Messages.Any(x => x.MessageLevel == MessageLevel.FatalError);

    /// <summary>
    ///     Adds a message to the operation result
    /// </summary>
    /// <param name="message"></param>
    public void AddMessage(OperationMessage message)
    {
        Messages.Add(message);
    }

    /// <summary>
    ///     Gets all messages as a string
    /// </summary>
    /// <returns></returns>
    public string GetMessages()
    {
        var stringBuilder = new StringBuilder();
        foreach (var compilerMessage in Messages)
        {
            stringBuilder.AppendLine(compilerMessage.ToString());
        }

        return stringBuilder.ToString();
    }

    /// <summary>
    ///     Writes all messages to the logger
    /// </summary>
    /// <param name="logger"></param>
    public void WriteMessagesToLogger(ILogger logger)
    {
        foreach (var compilerMessage in Messages)
        {
            switch (compilerMessage.MessageLevel)
            {
                case MessageLevel.Info:
                    logger.LogInformation("{Message}", compilerMessage.ToString());
                    break;
                case MessageLevel.Warning:
                    logger.LogWarning("{Message}", compilerMessage.ToString());
                    break;
                case MessageLevel.Error:
                    logger.LogError("{Message}", compilerMessage.ToString());
                    break;
                case MessageLevel.FatalError:
                    logger.LogCritical("{Message}", compilerMessage.ToString());
                    break;
            }
        }
    }
}