using System.Text.Json.Serialization;
using Meshmakers.Common.Shared;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts.Serialization;

namespace Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

/// <summary>
/// Represents a runtime type with attributes.
/// </summary>
public abstract class RtTypeWithAttributes
{
    // ReSharper disable once FieldCanBeMadeReadOnly.Local
    private Dictionary<string, object?> _attributes;

    /// <summary>
    ///     Constructor
    /// </summary>
    protected RtTypeWithAttributes()
    {
        _attributes = new Dictionary<string, object?>();
    }
    
    /// <summary>
    ///     Constructor
    /// </summary>
    protected RtTypeWithAttributes(IDictionary<string, object?> attributes)
    {
        _attributes = new Dictionary<string, object?>(attributes);
    }

    /// <summary>
    /// Returns a string that represents a location information for error messages
    /// </summary>
    /// <returns></returns>
    protected abstract string GetLocation();
    
    /// <summary>
    ///     Returns an dictionary of attributes.
    /// </summary>
    /// <remarks>
    ///     Vor getting/setting values use the GetAttribute/SetAttribute-Methods
    /// </remarks>
    public IReadOnlyDictionary<string, object?> Attributes => _attributes;

    /// <summary>
    /// Gets the attribute value or the standard value if the attribute is not set.
    /// This method allows non nullable types
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <param name="standardValue"></param>
    /// <typeparam name="TValue"></typeparam>
    /// <returns></returns>
    public TValue GetAttributeValueOrStandard<TValue>(string attributeName, TValue standardValue = default)
        where TValue : struct
    {
        if (!Attributes.TryGetValue(attributeName, out var value))
        {
            return standardValue;
        }

        if (value == null)
        {
            return standardValue;
        }

        // Because Convert.ChangeType cannot convert to enum types
        if (typeof(TValue).IsEnum)
        {
            return (TValue)Enum.ToObject(typeof(TValue), value);
        }

        return (TValue)Convert.ChangeType(value, typeof(TValue));
    }

    /// <summary>
    /// Gets the attribute value or the default value if the attribute is not set.
    /// This methods allows nullable types
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <param name="defaultValue"></param>
    /// <typeparam name="TValue"></typeparam>
    /// <returns></returns>
    // ReSharper disable once MemberCanBeProtected.Global
    public TValue? GetAttributeValueOrDefault<TValue>(string attributeName, TValue? defaultValue = default)
        where TValue : struct
    {
        if (!Attributes.TryGetValue(attributeName, out var value))
        {
            return defaultValue;
        }

        if (value == null)
        {
            return defaultValue;
        }

        // Because Convert.ChangeType cannot convert to enum types
        if (typeof(TValue).IsEnum)
        {
            return (TValue)Enum.ToObject(typeof(TValue), value);
        }

        return (TValue)Convert.ChangeType(value, typeof(TValue));
    }

    /// <summary>
    /// Gets the value of an attribute when the value is a list
    /// This method allows non nullable types
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <typeparam name="TValue"></typeparam>
    /// <returns></returns>
    public List<TValue> GetAttributeValues<TValue>(string attributeName)
    {
        if (!Attributes.TryGetValue(attributeName, out var value))
        {
            return new List<TValue>();
        }

        if (value == null)
        {
            return new List<TValue>();
        }

        if (value is List<TValue> list)
        {
            return list;
        }
        
        if (value is TValue[] ar)
        {
            return new List<TValue>(ar);
        }

        throw InvalidAttributeValueException.InvalidArrayValue(attributeName);
    }

    /// <summary>
    /// Gets the value of an attribute when the value is a list
    /// This methods allows nullable types
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <typeparam name="TValue"></typeparam>
    /// <returns></returns>
    public List<TValue>? GetAttributeValuesOrDefault<TValue>(string attributeName)
    {
        if (!Attributes.TryGetValue(attributeName, out var value))
        {
            return default;
        }

        if (value == null)
        {
            return default;
        }

        return new List<TValue>((IEnumerable<TValue>)Convert.ChangeType(value, typeof(IEnumerable<TValue>)));
    }

    /// <summary>
    /// Gets the value of an attribute when the value is non nullable
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <param name="defaultValue"></param>
    /// <typeparam name="TValue"></typeparam>
    /// <returns></returns>
    public TValue GetAttributeValue<TValue>(string attributeName, TValue defaultValue = default)
        where TValue : struct
    {
        if (!Attributes.TryGetValue(attributeName, out var value))
        {
            return defaultValue;
        }

        if (value == null)
        {
            return defaultValue;
        }

        // Because Convert.ChangeType cannot convert to enum types
        if (typeof(TValue).IsEnum)
        {
            return (TValue)Enum.ToObject(typeof(TValue), value);
        }

        return (TValue)Convert.ChangeType(value, typeof(TValue));
    }

    /// <summary>
    /// Gets the value of an attribute if the value is nullable
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <param name="defaultValue"></param>
    /// <returns></returns>
    public object? GetAttributeValueOrDefault(string attributeName, object? defaultValue = default)
    {
        if (!Attributes.TryGetValue(attributeName, out var value))
        {
            return defaultValue;
        }

        return value;
    }

    /// <summary>
    /// Gets the value of an attribute if the value is nullable and a string
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <param name="defaultValue"></param>
    /// <returns></returns>
    public string? GetAttributeStringValueOrDefault(string attributeName, string? defaultValue = default)
    {
        if (!Attributes.TryGetValue(attributeName, out var value))
        {
            return defaultValue;
        }

        return (string?)value;
    }

    /// <summary>
    /// Gets the value of an attribute if the value is non-nullable and a string
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public string GetAttributeStringValue(string attributeName)
    {
        if (!Attributes.TryGetValue(attributeName, out var value) || value == null)
        {
            throw InvalidAttributeValueException.CannotBeNull(GetLocation(), attributeName);
        }

        return (string)value;
    }

    /// <summary>
    /// Sets the value of an attribute when the value is nullable
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <param name="attributeValueTypes">Type of attribute value</param>
    /// <param name="attributeValue">The value of the attribute</param>
    public void SetAttributeValue(string attributeName, AttributeValueTypesDto attributeValueTypes,
        object? attributeValue)
    {
        _attributes[attributeName] = AttributeValueConverter.ConvertAttributeValue(attributeValueTypes, attributeValue);
    }

    /// <summary>
    /// Sets the value of an attribute when the value is non nullable
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <param name="attributeValueTypes">Type of attribute value</param>
    /// <param name="attributeValue">The value of the attribute</param>
    public void SetAttributeValueNonNullable(string attributeName, AttributeValueTypesDto attributeValueTypes,
        object attributeValue)
    {
        _attributes[attributeName] = AttributeValueConverter.ConvertAttributeValue(attributeValueTypes, attributeValue);
    }
}