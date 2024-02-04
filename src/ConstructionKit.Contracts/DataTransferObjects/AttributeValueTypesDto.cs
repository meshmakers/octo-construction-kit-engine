namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     The type of an attribute value.
/// </summary>
public enum AttributeValueTypesDto
{
    /// <summary>
    ///     Indicates that the attribute value is an integer.
    /// </summary>
    Int = 1,

    /// <summary>
    ///     Indicates that the attribute value is a string.
    /// </summary>
    String = 2,

    /// <summary>
    /// Small binary data (less than 1MB)
    /// </summary>
    Binary = 3,

    /// <summary>
    ///     Indicates that the attribute value is a boolean.
    /// </summary>
    Boolean = 4,

    /// <summary>
    ///     Indicates that the attribute value is a date time.
    /// </summary>
    DateTime = 5,

    /// <summary>
    ///     Indicates that the attribute value is a double.
    /// </summary>
    Double = 6,

    /// <summary>
    ///     Indicates that the attribute value is a string array.
    /// </summary>
    StringArray = 7,

    /// <summary>
    ///     Indicates that the attribute value is an integer array.
    /// </summary>
    IntArray = 8,

    /// <summary>
    ///     Indicates that the attribute value is a binary file in a storage linked to the attribute.
    /// </summary>
    BinaryLinked = 9,

    /// <summary>
    ///     Indicates that the attribute value is a complex object - a so called record.
    /// </summary>
    Record = 10,

    /// <summary>
    ///     Indicates that the attribute value is an array of complex objects - so called records.
    /// </summary>
    RecordArray = 11,

    /// <summary>
    ///     Indicates that the attribute value is a time span.
    /// </summary>
    TimeSpan = 12,

    /// <summary>
    ///     Indicates that the attribute value is an enum.
    /// </summary>
    Enum = 13,

    /// <summary>
    ///     Indicates that the attribute value is a 64 bit integer.
    /// </summary>
    Int64 = 14,

    /// <summary>
    ///     Indicates that the attribute value is a date time offset.
    /// </summary>
    DateTimeOffset = 15
}