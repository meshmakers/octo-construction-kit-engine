
// ReSharper disable CollectionNeverQueried.Global

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.Runtime.Contracts.DataTransferObjects;

/// <summary>
///     Defines a type (entity or record) with attributes for runtime model 
/// </summary>
public class RtTypeWithAttributesDto
{
    /// <summary>
    ///     Creates a new instance of <see cref="RtTypeWithAttributesDto" />
    /// </summary>
    // ReSharper disable once MemberCanBeProtected.Global
    public RtTypeWithAttributesDto()
    {
        Attributes = new List<RtAttributeDto>();
    }

    /// <summary>
    ///     Gets or sets the attributes of the type
    /// </summary>
    public List<RtAttributeDto> Attributes { get; set; }
}