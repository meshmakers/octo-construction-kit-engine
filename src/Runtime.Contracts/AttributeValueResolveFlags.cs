namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
/// Flags that define how attribute values should be resolved
/// </summary>
public enum AttributeValueResolveFlags
{
    /// <summary>
    /// Default behavior, no special resolution
    /// </summary>
    Default = 0,

    /// <summary>
    /// Resolves enum keys to their names
    /// </summary>
    ResolveEnumsToNames = 1,
}