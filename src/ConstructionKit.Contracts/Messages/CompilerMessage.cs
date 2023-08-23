namespace Meshmakers.Octo.ConstructionKit.Contracts.Messages;

/// <summary>
/// Contains a concrete message
/// </summary>
public class CompilerMessage
{
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="messageLevel">Message level</param>
    /// <param name="messageNumber">Message number</param>
    /// <param name="messageText">Message text</param>
    public CompilerMessage(MessageLevel messageLevel, int messageNumber, string messageText)
    {
        CreateDateTime = DateTime.Now;
        MessageLevel = messageLevel;
        MessageNumber = messageNumber;
        MessageText = messageText;
    }


    /// <summary>
    /// Returns the level
    /// </summary>
    public DateTime CreateDateTime { get; }

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

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{CreateDateTime.ToShortTimeString()} {MessageLevel} {MessageNumber}: {MessageText}";
    }
}