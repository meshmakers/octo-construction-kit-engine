namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
///     Defines if an operation creates, updates or deletes
/// </summary>
public enum EntityModOptions
{
    /// <summary>
    ///     Insert entity
    /// </summary>
    Insert = 0,

    /// <summary>
    ///     Delete entity
    /// </summary>
    Delete = 1,

    /// <summary>
    ///     Update entity
    /// </summary>
    Update = 2,

    /// <summary>
    ///     Replace entity
    /// </summary>
    Replace = 3
}