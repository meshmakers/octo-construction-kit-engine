using System.Diagnostics;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
/// Represents a query column in a type query.
/// </summary>
[DebuggerDisplay("{" + nameof(Path) + "}")]
public class CkTypeQueryColumn
{
    /// <summary>
    /// Creates a new instance of <see cref="CkTypeQueryColumn"/>.
    /// </summary>
    /// <param name="path">Path to the column within a type query.</param>
    /// <param name="accessPathList">Access paths to the column as a list of single properties.</param>
    /// <param name="valueType">Type of the column.</param>
    public CkTypeQueryColumn(string path, IEnumerable<PathTerm> accessPathList, AttributeValueTypesDto valueType)
    {
        Path = path;
        AccessPathList = accessPathList;
        ValueType = valueType;
    }

    /// <summary>
    /// Represents a path to a column within a type query.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Returns the access path to the column as a list of single properties.
    /// </summary>
    public IEnumerable<PathTerm> AccessPathList { get; }

    /// <summary>
    /// Represents the type of the column.
    /// </summary>
    public AttributeValueTypesDto ValueType { get; }
}