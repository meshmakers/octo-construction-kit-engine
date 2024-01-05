namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Interface of a versioned construction kit element id
/// </summary>
public interface ICkKey : IConvertible
{
    /// <summary>
    ///     Returns the full name of the key including the complete version number
    /// </summary>
    string FullName { get; }

    /// <summary>
    ///     Returns the full name of the key, including the semantic version. That means,
    ///     Major version is included except for major version 1
    /// </summary>
    string SemanticVersionedFullName { get; }

    /// <summary>
    ///     Returns true, if the key is empty.
    /// </summary>
    bool IsEmpty { get; }
}