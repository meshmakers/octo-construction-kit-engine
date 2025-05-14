using System.Diagnostics;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Represents a path term
/// </summary>
/// <param name="value">Value of the term</param>
/// <param name="type">Type of the term</param>
[DebuggerDisplay("{Value} ({Type})")]
public class PathTerm(string value, PathType type)
{
    /// <summary>
    /// Returns the value of the term
    /// </summary>
    public string Value { get; } = value;

    /// <summary>
    /// Returns the type of the term
    /// </summary>
    public PathType Type { get; } = type;
}