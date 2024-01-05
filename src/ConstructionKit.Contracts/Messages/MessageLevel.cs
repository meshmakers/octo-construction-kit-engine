namespace Meshmakers.Octo.ConstructionKit.Contracts.Messages;

/// <summary>
///     Defines possible message level
/// </summary>
public enum MessageLevel
{
    /// <summary>
    ///     Information message
    /// </summary>
    Info = 0,

    /// <summary>
    ///     Warning message
    /// </summary>
    Warning = 1,

    /// <summary>
    ///     Error message
    /// </summary>
    Error = 2,

    /// <summary>
    ///     Fatal error message
    /// </summary>
    FatalError = 3
}