using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

/// <summary>
///     Represents a runtime type with attributes.
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
    protected RtTypeWithAttributes(IReadOnlyDictionary<string, object?> attributes)
    {
#if NETSTANDARD2_0
        _attributes = new Dictionary<string, object?>(attributes
            .ToDictionary(k => k.Key, v => v.Value));
#else
        _attributes = new Dictionary<string, object?>(attributes);
#endif
    }

    /// <summary>
    ///     Returns an dictionary of attributes.
    /// </summary>
    /// <remarks>
    ///     Vor getting/setting values use the GetAttribute/SetAttribute-Methods
    /// </remarks>
    public IReadOnlyDictionary<string, object?> Attributes => _attributes;

    /// <summary>
    ///     Returns a string that represents a location information for error messages
    /// </summary>
    /// <returns></returns>
    protected abstract string GetLocation();

    /// <summary>
    ///     Gets the attribute value or the standard value if the attribute is not set.
    ///     This method allows non nullable types
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
    ///     Gets the attribute value or the default value if the attribute is not set.
    ///     This methods allows nullable types
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
    ///     Gets the value of an attribute when the value is a list
    ///     This method allows non nullable types
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <typeparam name="TValue"></typeparam>
    /// <returns></returns>
    public IAttributeValueList<TValue> GetAttributeValues<TValue>(string attributeName)
        where TValue : struct
    {
        if (!Attributes.TryGetValue(attributeName, out var value) || value == null)
        {
            var newList = new AttributePrimitiveValueList<TValue>();
            _attributes[attributeName] = newList.InnerList;
            return newList;
        }

        if (value is List<TValue> list)
        {
            return new AttributePrimitiveValueList<TValue>(list);
        }

        if (value is List<object> objList) // This code is needed because MongoDB is deserializing empty arrays with List<object>
        {
            var primitiveList = objList.Cast<TValue>().ToList();
            _attributes[attributeName] = primitiveList;
            return new AttributePrimitiveValueList<TValue>(primitiveList);
        }

        throw InvalidAttributeValueException.InvalidArrayValue(attributeName, typeof(TValue));
    }

    /// <summary>
    ///     Gets the value of an attribute when the value is a list
    ///     This method allows non nullable types
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <returns></returns>
    public IAttributeValueList<string> GetAttributeStringValues(string attributeName)
    {
        if (!Attributes.TryGetValue(attributeName, out var value) || value == null)
        {
            var newList = new AttributeStringValueList();
            _attributes[attributeName] = newList.InnerList;
            return newList;
        }

        if (value is List<string> list)
        {
            return new AttributeStringValueList(list);
        }

        if (value is List<object> objList) // This code is needed because MongoDB is deserializing empty arrays with List<object>
        {
            var strings = objList.Cast<string>().ToList();
            _attributes[attributeName] = strings;
            return new AttributeStringValueList(strings);
        }

        throw InvalidAttributeValueException.InvalidArrayValue(attributeName, typeof(string));
    }


    /// <summary>
    ///     Gets the value of an attribute when the value is a list
    ///     This method allows non nullable types
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <typeparam name="TValue"></typeparam>
    /// <returns></returns>
    public IAttributeValueList<TValue> GetRtRecordAttributeValues<TValue>(string attributeName)
        where TValue : RtRecord, new()
    {
        if (!Attributes.TryGetValue(attributeName, out var value) || value == null)
        {
            var newList = new AttributeRecordValueList<TValue>();
            _attributes[attributeName] = newList.InnerList;
            return newList;
        }

        if (value is List<RtRecord> list)
        {
            return new AttributeRecordValueList<TValue>(list);
        }

        if (value is List<object> objList) // This code is needed because MongoDB is deserializing empty arrays with List<object>
        {
            var rtRecords = objList.Cast<RtRecord>().ToList();
            _attributes[attributeName] = rtRecords;
            return new AttributeRecordValueList<TValue>(rtRecords);
        }

        throw InvalidAttributeValueException.InvalidArrayValue(attributeName, typeof(TValue));
    }

    /// <summary>
    ///     Gets the value of an attribute when the value is a list
    ///     This methods allows nullable types
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <typeparam name="TValue"></typeparam>
    /// <returns></returns>
    public IAttributeValueList<TValue>? GetAttributeValuesOrDefault<TValue>(string attributeName)
        where TValue : struct
    {
        if (!Attributes.TryGetValue(attributeName, out var value))
        {
            return default;
        }

        if (value == null)
        {
            return default;
        }

        if (value is List<TValue> list)
        {
            return new AttributePrimitiveValueList<TValue>(list);
        }

        if (value is List<object> objList) // This code is needed because MongoDB is deserializing empty arrays with List<object>
        {
            var primitiveList = objList.Cast<TValue>().ToList();
            _attributes[attributeName] = primitiveList;
            return new AttributePrimitiveValueList<TValue>(primitiveList);
        }

        throw InvalidAttributeValueException.InvalidArrayValue(attributeName, typeof(TValue));
    }

    /// <summary>
    ///     Gets the value of an attribute when the value is a list
    ///     This method allows non nullable types
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <typeparam name="TValue"></typeparam>
    /// <returns></returns>
    public IAttributeValueList<TValue>? GetRtRecordAttributeValuesOrDefault<TValue>(string attributeName)
        where TValue : RtRecord, new()
    {
        if (!Attributes.TryGetValue(attributeName, out var value))
        {
            return default;
        }

        if (value == null)
        {
            return default;
        }

        if (value is List<RtRecord> list)
        {
            return new AttributeRecordValueList<TValue>(list);
        }

        if (value is List<object> objList) // This code is needed because MongoDB is deserializing empty arrays with List<object>
        {
            var rtRecords = objList.Cast<RtRecord>().ToList();
            _attributes[attributeName] = rtRecords;
            return new AttributeRecordValueList<TValue>(rtRecords);
        }

        throw InvalidAttributeValueException.InvalidArrayValue(attributeName, typeof(TValue));
    }

    /// <summary>
    ///     Gets the value of an attribute when the value is a list
    ///     This method allows non nullable types
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <returns></returns>
    public IAttributeValueList<string>? GetAttributeStringValuesOrDefault(string attributeName)
    {
        if (!Attributes.TryGetValue(attributeName, out var value))
        {
            return default;
        }

        if (value == null)
        {
            return default;
        }

        if (value is List<string> list)
        {
            return new AttributeStringValueList(list);
        }

        if (value is List<object> objList) // This code is needed because MongoDB is deserializing empty arrays with List<object>
        {
            var strings = objList.Cast<string>().ToList();
            _attributes[attributeName] = strings;
            return new AttributeStringValueList(strings);
        }

        throw InvalidAttributeValueException.InvalidArrayValue(attributeName, typeof(string));
    }

    /// <summary>
    ///     Gets the value of an attribute when the value is non nullable
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

        if (typeof(TValue) == typeof(TimeSpan) && value is string)
        {
            return (TValue)Convert.ChangeType(TimeSpan.Parse((string)value), typeof(TValue));
        }

        return (TValue)Convert.ChangeType(value, typeof(TValue));
    }

    /// <summary>
    ///     Gets the value of an attribute if the value is nullable
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
    ///     Gets the value of an RtRecord attribute when the value is non nullable
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <typeparam name="TValue"></typeparam>
    /// <returns></returns>
    public TValue GetRtRecordAttributeValue<TValue>(string attributeName)
        where TValue : RtRecord, new()
    {
        if (!Attributes.TryGetValue(attributeName, out var value))
        {
            throw InvalidAttributeValueException.CannotBeNull(GetLocation(), attributeName);
        }

        if (value is TValue recordSpecialized)
        {
            return recordSpecialized;
        }

        if (value is RtRecord rtRecord)
        {
            var x = (TValue?)Activator.CreateInstance(typeof(TValue), rtRecord);
            if (x == null)
            {
                throw InvalidAttributeValueException.CannotActivateInstance(typeof(TValue));
            }
        }

        throw InvalidAttributeValueException.InvalidRecordValue(attributeName, typeof(TValue));
    }

    /// <summary>
    ///     Gets the value of an RtRecord attribute when the value is non nullable
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <typeparam name="TValue"></typeparam>
    /// <returns></returns>
    public TValue? GetRtRecordAttributeValueOrDefault<TValue>(string attributeName)
        where TValue : RtRecord, new()
    {
        if (!Attributes.TryGetValue(attributeName, out var value))
        {
            return null;
        }

        if (value == null)
        {
            return null;
        }

        if (value is TValue recordSpecialized)
        {
            return recordSpecialized;
        }

        if (value is RtRecord rtRecord)
        {
            var x = (TValue?)Activator.CreateInstance(typeof(TValue), rtRecord);
            if (x == null)
            {
                throw InvalidAttributeValueException.CannotActivateInstance(typeof(TValue));
            }
        }

        throw InvalidAttributeValueException.InvalidRecordValue(attributeName, typeof(TValue));
    }


    /// <summary>
    ///     Gets the value of an attribute if the value is nullable and a string
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
    ///     Gets the value of an attribute if the value is non-nullable and a string
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
    ///     Sets the value of an attribute when the value is nullable
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
    ///     Sets the value of an attribute when the value is non nullable
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