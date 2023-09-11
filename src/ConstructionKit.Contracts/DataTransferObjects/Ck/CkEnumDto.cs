using System.Diagnostics;
using System.Text.Json.Serialization;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;

/// <summary>
/// Describes a construction kit enum that is used as enum type of an attribute
/// </summary>
[DebuggerDisplay("{" + nameof(EnumId) + "}")]
public class CkEnumDto
{
    /// <summary>
    /// Creates a new instance of <see cref="CkEnumDto"/>.
    /// </summary>
    public CkEnumDto()
    {
        Values = new List<CkEnumValueDto>();
    }
    
    /// <summary>
    ///     Gets or sets the construction kit id
    /// </summary>
    [JsonRequired]
    public CkEnumId EnumId { get; set; }
    
    /// <summary>
    /// When true the enum is handles as flags enum
    /// </summary>
    public bool UseFlags {get; set; }

    /// <summary>
    /// Values of the enum
    /// </summary>
    public ICollection<CkEnumValueDto> Values { get; set; }
}