
// ReSharper disable CollectionNeverQueried.Global

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.Runtime.Contracts.DataTransferObjects;

/// <summary>
///     Defines an type with attributes for the runtime 
/// </summary>
public class RtTypeWithAttributesDto
{
    /// <summary>
    ///     Creates a new instance of <see cref="RtTypeWithAttributesDto" />
    /// </summary>
    public RtTypeWithAttributesDto()
    {
        Attributes = new List<RtAttributeDto>();
    }

    /// <summary>
    ///     Gets or sets the attributes of the type
    /// </summary>
    public List<RtAttributeDto> Attributes { get; set; }
}