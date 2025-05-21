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
    /// <param name="associationTuple">When the value type is association,
    /// this represents the association direction tuple of the column.</param>
    public CkTypeQueryColumn(string path, IEnumerable<PathTerm> accessPathList, AttributeValueTypesDto valueType, CkTypeAssociationTuple? associationTuple = null)
    {
        Path = path;
        AccessPathList = accessPathList;
        ValueType = valueType;
        AssociationTuple = associationTuple;
    }

    /// <summary>
    /// Creates a new instance of <see cref="CkTypeQueryColumn"/>.
    /// </summary>
    /// <param name="path">Path to the column within a type query.</param>
    /// <param name="accessPathList">Access paths to the column as a list of single properties.</param>
    /// <param name="ckEnumId">The enum id of the column if the value type is an enum.</param>
    public CkTypeQueryColumn(string path, IEnumerable<PathTerm> accessPathList,  CkId<CkEnumId> ckEnumId)
    {
        Path = path;
        AccessPathList = accessPathList;
        ValueType = AttributeValueTypesDto.Enum;
        CkEnumId = ckEnumId;
    }

    /// <summary>
    /// Creates a new instance of <see cref="CkTypeQueryColumn"/>.
    /// </summary>
    /// <param name="path">Path to the column within a type query.</param>
    /// <param name="accessPathList">Access paths to the column as a list of single properties.</param>
    /// <param name="isArray">Whether the column is an array.</param>
    /// <param name="valueType">Type of the column.</param>
    public CkTypeQueryColumn(string path, IEnumerable<PathTerm> accessPathList, bool isArray, AttributeValueTypesDto valueType)
    {
        Path = path;
        AccessPathList = accessPathList;
        ValueType = isArray ? AttributeValueTypesDto.RecordArray : AttributeValueTypesDto.Record;
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

    /// <summary>
    /// Returns the association direction tuple of the column if the value type is an association.
    /// </summary>
    public CkTypeAssociationTuple? AssociationTuple { get; }

    /// <summary>
    /// Returns the enum id of the column if the value type is an enum.
    /// </summary>
    public CkId<CkEnumId>? CkEnumId  { get; }
}