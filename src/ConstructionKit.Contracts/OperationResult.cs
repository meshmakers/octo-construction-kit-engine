using System.Text;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

public class OperationResult
{
    public OperationResult()
    {
        Messages = new();
    }

    public List<CompilerMessage> Messages { get; }

    public bool HasErrors => Messages.Any(x => x.MessageLevel == MessageLevel.Error);
    public bool HasFatalErrors => Messages.Any(x => x.MessageLevel == MessageLevel.FatalError);

    public void AddMessage(CompilerMessage message)
    {
        Messages.Add(message);
    }
    
    public string GetMessages()
    {
        var stringBuilder = new StringBuilder();
        foreach (var compilerMessage in Messages)
        {
            stringBuilder.AppendLine(compilerMessage.ToString());
        }

        return stringBuilder.ToString();
    }

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