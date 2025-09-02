using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
/// Thrown when an enum value is not found in the Construction Kit
/// </summary>
public class CkEnumValueNotFoundException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CkEnumValueNotFoundException"/> class.
    /// </summary>
    private CkEnumValueNotFoundException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CkEnumValueNotFoundException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    private CkEnumValueNotFoundException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CkEnumValueNotFoundException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="inner">The inner exception</param>
    private CkEnumValueNotFoundException(string message, Exception inner) : base(message, inner)
    {
    }

    /// <summary>
    /// Gets the CkEnumId for which the enum value was not found.
    /// </summary>
    public required CkId<CkEnumId> CkEnumId { get; init; }

    /// <summary>
    /// Gets the enum value that was not found.
    /// </summary>
    public required object EnumValue { get; init; }

    internal static Exception EnumValueNotFound(CkId<CkEnumId> valueCkEnumId, object value)
    {
        return new CkEnumValueNotFoundException(
            $"Enum value '{value}' not found for CkEnumId '{valueCkEnumId}'. Ensure that the value is defined in the CkEnum.")
        {
            CkEnumId = valueCkEnumId,
            EnumValue = value
        };
    }
}