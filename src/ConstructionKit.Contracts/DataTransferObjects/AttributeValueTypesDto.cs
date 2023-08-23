namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
/// The type of an attribute value.
/// </summary>
public enum AttributeValueTypesDto
{
    /// <summary>
    /// Indicates that the attribute value is an integer.
    /// </summary>
    Int = 1,
    
    /// <summary>
    /// Indicates that the attribute value is a string.
    /// </summary>
    String = 2,

    //  Binary = 3,
    
    /// <summary>
    /// Indicates that the attribute value is a boolean.
    /// </summary>
    Boolean = 4,
    
    /// <summary>
    /// Indicates that the attribute value is a date time.
    /// </summary>
    DateTime = 5,
    
    /// <summary>
    /// Indicates that the attribute value is a double.
    /// </summary>
    Double = 6,

    /// <summary>
    /// Indicates that the attribute value is a binary file in a storage linked to the attribute
    /// </summary>
    BinaryLinked = 9
}
