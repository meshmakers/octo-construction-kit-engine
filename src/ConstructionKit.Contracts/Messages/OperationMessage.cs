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
        Parameters = EmptyParameters;
    }

    /// <summary>
    ///     Constructor
    /// </summary>
    /// <param name="messageLevel">Message level</param>
    /// <param name="location">The location, if any exists</param>
    /// <param name="messageNumber">Message number</param>
    /// <param name="messageText">Message text</param>
    /// <param name="parameters">The values of the placeholders of the message, by placeholder name</param>
    public OperationMessage(MessageLevel messageLevel, string? location, int messageNumber, string messageText,
        IReadOnlyDictionary<string, string> parameters)
        : this(messageLevel, location, messageNumber, messageText)
    {
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyParameters =
        new Dictionary<string, string>();


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

    /// <summary>
    ///     Returns the values of the placeholders of the message text, by placeholder name. This is the
    ///     machine-readable contract of a message: tools branch on <see cref="MessageNumber" /> and these parameters
    ///     (for example the stable <c>reason</c> token of message 133), never on <see cref="MessageText" />, which is
    ///     for humans and may be reworded. Empty when the message was created without parameters.
    /// </summary>
    public IReadOnlyDictionary<string, string> Parameters { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{CreateDateTime.ToShortTimeString()} {MessageLevel} {MessageNumber} {Location}: {MessageText}";
    }
}