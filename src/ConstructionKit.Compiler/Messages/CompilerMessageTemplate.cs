using Meshmakers.Octo.ConstructionKit.Contracts.Messages;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Messages;

/// <summary>
/// Represents a message template
/// </summary>
internal class CompilerMessageTemplate
{
    /// <summary>
    /// Returns the level
    /// </summary>
    public MessageLevel MessageLevel { get; }

    /// <summary>
    /// Returns the number
    /// </summary>
    public int MessageNumber { get; }

    /// <summary>
    /// Returns a message text
    /// </summary>
    public string MessageText { get; }

    /// <summary>
    /// Returns the placeholders of the message text
    /// </summary>
    public string[] Placeholders { get; }

    /// <summary>
    /// c'tor
    /// </summary>
    /// <param name="messageLevel">Message level</param>
    /// <param name="messageNumber">Message number</param>
    /// <param name="messageText">Message text</param>
    /// <param name="placeholders">Placeholders of the message text</param>
    public CompilerMessageTemplate(MessageLevel messageLevel, int messageNumber, string messageText, string[] placeholders)
    {
        MessageLevel = messageLevel;
        MessageNumber = messageNumber;
        MessageText = messageText;
        Placeholders = placeholders;
    }

    /// <summary>
    /// Returns the formatted message
    /// </summary>
    /// <param name="args">A list of arguments for string f</param>
    /// <returns>The formatted message</returns>
    public CompilerMessage CreateMessage(params object[] args)
    {
        var text = MessageText;
        for (int i = 0; i < Placeholders.Length; i++)
        {
            text = text.Replace($"{{{Placeholders[i]}}}", $"{{{i}}}");
        }

        return new CompilerMessage(MessageLevel, MessageNumber, string.Format(text, args));
    }
}