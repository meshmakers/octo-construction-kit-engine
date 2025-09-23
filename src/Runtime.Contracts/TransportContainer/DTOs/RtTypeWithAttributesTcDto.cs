
// ReSharper disable CollectionNeverQueried.Global

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;

/// <summary>
///     Defines a type (entity or record) with attributes for runtime model 
/// </summary>
public class RtTypeWithAttributesTcDto
{
    /// <summary>
    ///     Creates a new instance of <see cref="RtTypeWithAttributesTcDto" />
    /// </summary>
    // ReSharper disable once MemberCanBeProtected.Global
    public RtTypeWithAttributesTcDto()
    {
        Attributes = [];
    }

    /// <summary>
    ///     Gets or sets the attributes of the type
    /// </summary>
    public List<RtAttributeTcDto> Attributes { get; set; }
}