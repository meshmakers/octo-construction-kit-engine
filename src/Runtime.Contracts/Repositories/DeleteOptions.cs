namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
/// Defines options for delete operations
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public record DeleteOptions
{
    /// <summary>
    /// Defines the type of delete operation
    /// </summary>
    public DeleteStrategies Strategy { get; init; } = DeleteStrategies.Archive;

    /// <summary>
    /// Returns the default options for delete operations.
    /// </summary>
    public static DeleteOptions Default => new();

    /// <summary>
    /// Returns options that erases entities during delete
    /// </summary>
    public static DeleteOptions Erase = new() { Strategy = DeleteStrategies.Erase };
}

