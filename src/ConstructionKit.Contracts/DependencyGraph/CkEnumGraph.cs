using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
///     Represents a construction kit enum in the dependency graph
/// </summary>
[DebuggerDisplay("{" + nameof(CkEnumId) + "}")]
public class CkEnumGraph
{
    /// <summary>
    ///     Creates a new instance of <see cref="CkEnumGraph" />.
    /// </summary>
    /// <param name="ckEnumId"></param>
    /// <param name="enumDto"></param>
    public CkEnumGraph(CkId<CkEnumId> ckEnumId, CkEnumDto enumDto)
    {
        CkEnumId = ckEnumId;
        UseFlags = enumDto.UseFlags;
        IsExtensible = enumDto.IsExtensible;
        Values = enumDto.Values;
        Description = enumDto.Description;
    }

    /// <summary>
    ///     Creates a new instance of <see cref="CkEnumGraph" />.
    /// </summary>
    /// <param name="ckEnumId"></param>
    /// <param name="useFlags"></param>
    /// <param name="isExtensible"></param>
    /// <param name="values"></param>
    /// <param name="description"></param>
    [JsonConstructor]
    public CkEnumGraph(CkId<CkEnumId> ckEnumId, bool useFlags, bool isExtensible, ICollection<CkEnumValueDto> values, string description)
    {
        CkEnumId = ckEnumId;
        UseFlags = useFlags;
        Values = values;
        Description = description;
    }

    /// <summary>
    ///     Gets or sets the construction kit id
    /// </summary>
    public CkId<CkEnumId> CkEnumId { get; }

    /// <summary>
    ///     When true the enum is handles as flags enum
    /// </summary>
    public bool UseFlags { get; set; }
    
    /// <summary>
    ///     When true the enum is extensible using the API
    /// </summary>
    public bool IsExtensible { get; set; }

    /// <summary>
    ///     Returns the values of the enum.
    /// </summary>
    public ICollection<CkEnumValueDto> Values { get; }
    
    /// <summary>
    ///     An optional description of the enum
    /// </summary>
    public string? Description { get; set; }
}