namespace Meshmakers.Octo.ConstructionKit.Contracts.Messages;

/// <summary>
///     Contains a concrete message
/// </summary>
public class OperationMessage
{
    /// <summary>
    ///     Constructor
    /// </summary>
    /// <param name="messageLevel">Message level</param>
    /// <param name="location">The location, if any exists</param>
    /// <param name="messageNumber">Message number</param>
    /// <param name="messageText">Message text</param>
    public OperationMessage(MessageLevel messageLevel, string? location, int messageNumber, string messageText)
    {
        CreateDateTime = DateTime.Now;
        MessageLevel = messageLevel;
        Location = location;
        MessageNumber = messageNumber;
        MessageText = messageText;
    }


    /// <summary>
    ///     Returns the level
    /// </summary>
    public DateTime CreateDateTime { get; }

    /// <summary>
    ///     Returns the level
    /// </summary>
    public MessageLevel MessageLevel { get; }

    /// <summary>
    ///    Returns the location, if any exists
    /// </summary>
    public string? Location { get; }

    /// <summary>
    ///     Returns the number
    /// </summary>
    public int MessageNumber { get; }

    /// <summary>
    ///     Returns a message text
    /// </summary>
    public string MessageText { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{CreateDateTime.ToShortTimeString()} {MessageLevel} {MessageNumber} {Location}: {MessageText}";
    }
}