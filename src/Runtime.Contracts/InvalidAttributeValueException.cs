namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
/// Throws when an invalid attribute value is set.
/// </summary>
public class InvalidAttributeValueException : PersistenceException
{
    /// <summary>
    /// Creates a new instance of <see cref="InvalidAttributeValueException"/>.
    /// </summary>
    private InvalidAttributeValueException()
    {
    }

    /// <summary>
    /// Creates a new instance of <see cref="InvalidAttributeValueException"/>.
    /// </summary>
    private InvalidAttributeValueException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new instance of <see cref="InvalidAttributeValueException"/>.
    /// </summary>
    private InvalidAttributeValueException(string message, Exception inner) : base(message, inner)
    {
    }

    internal static Exception CannotBeNull(string location, string attributeName)
    {
        return new InvalidAttributeValueException($"Attribute value cannot be null for '{location}' at attribute with name '{attributeName}'");
    }

    internal static Exception InvalidArrayValue(string attributeName, Type elementType)
    {
        return new InvalidAttributeValueException($"Attribute value must be an array or list of type '{elementType}' for attribute with name '{attributeName}'");
    }

    internal static Exception CannotActivateInstance(Type type)
    {
        return new InvalidAttributeValueException($"Cannot activate instance of type '{type}'");
    }

    internal static Exception InvalidRecordValue(string attributeName, Type type)
    {
        return new InvalidAttributeValueException($"Cannot convert to type '{type}' at attribute with name '{attributeName}'");
    }
}