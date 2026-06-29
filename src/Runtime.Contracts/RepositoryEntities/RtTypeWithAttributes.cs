using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts.Geospatial.Geometry;

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
    ///     Returns a dictionary of attributes.
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

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{GetType().Name}: {GetLocation()}";
    }

    /// <summary>
    ///     Gets the attribute value or the standard value if the attribute is not set.
    ///     This method allows non-nullable types
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
    ///     This method allow nullable types
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <param name="defaultValue"></param>
    /// <typeparam name="TValue"></typeparam>
    /// <returns></returns>
    // ReSharper disable once MemberCanBeProtected.Global
    public TValue? GetAttributeValueOrDefault<TValue>(string attributeName, TValue? defaultValue = null)
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

        if (typeof(TValue) == typeof(TimeSpan) && TryCoerceTimeSpan(value, out var timeSpan))
        {
            return (TValue)(object)timeSpan;
        }

        return (TValue)Convert.ChangeType(value, typeof(TValue));
    }

    /// <summary>
    ///     Coerces a Mongo-deserialised attribute value to <see cref="TimeSpan"/>. Accepts the value
    ///     as-is, a tick count (Int64/Int32), a bare-integer tick count rendered as a string
    ///     (<c>"9000000000"</c> — the shape ImportRt produces when a TimeSpan attribute is
    ///     round-tripped through the export/import JSON, AB#4259), or a string in either .NET
    ///     (<c>00:15:00</c>) or ISO-8601 (<c>PT15M</c>) format. <see cref="Convert.ChangeType(object, Type)"/>
    ///     on its own does not handle any of these conversions and would throw
    ///     <see cref="InvalidCastException"/> on the <c>Dictionary&lt;string, object?&gt;</c> values that
    ///     the runtime engine stores — they round-trip through <c>OctoObjectSerializer</c> which
    ///     dispatches on BSON type, not on the consumer's expected CLR type, so strings/long stay
    ///     strings/long after read.
    /// </summary>
    private static bool TryCoerceTimeSpan(object value, out TimeSpan timeSpan)
    {
        switch (value)
        {
            case TimeSpan ts:
                timeSpan = ts;
                return true;
            case long ticks:
                timeSpan = TimeSpan.FromTicks(ticks);
                return true;
            case int ticks32:
                timeSpan = TimeSpan.FromTicks(ticks32);
                return true;
            // A bare-integer string is the canonical ticks form (matching the Int64/Int32 cases
            // above), NOT a .NET TimeSpan literal — TimeSpan.Parse would read "9000000000" as
            // 9-billion *days* and overflow. Must come before the TimeSpan.TryParse branch.
            case string s when long.TryParse(s, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var tickString):
                timeSpan = TimeSpan.FromTicks(tickString);
                return true;
            case string s when TimeSpan.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var parsed):
                timeSpan = parsed;
                return true;
            case string s:
                try
                {
                    timeSpan = System.Xml.XmlConvert.ToTimeSpan(s);
                    return true;
                }
                catch (FormatException)
                {
                    timeSpan = default;
                    return false;
                }
            default:
                timeSpan = default;
                return false;
        }
    }

    /// <summary>
    ///     Gets the value of an attribute when the value is a list
    ///     This method allows non-nullable types
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
    ///     This method allows non-nullable types
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
    ///     This method allows non-nullable types
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
    ///     This method allows nullable types
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <typeparam name="TValue"></typeparam>
    /// <returns></returns>
    public IAttributeValueList<TValue>? GetAttributeValuesOrDefault<TValue>(string attributeName)
        where TValue : struct
    {
        if (!Attributes.TryGetValue(attributeName, out var value))
        {
            return null;
        }

        if (value == null)
        {
            return null;
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
    ///     This method allows non-nullable types
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <typeparam name="TValue"></typeparam>
    /// <returns></returns>
    public IAttributeValueList<TValue>? GetRtRecordAttributeValuesOrDefault<TValue>(string attributeName)
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
    ///     This method allows non-nullable types
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <returns></returns>
    public IAttributeValueList<string>? GetAttributeStringValuesOrDefault(string attributeName)
    {
        if (!Attributes.TryGetValue(attributeName, out var value))
        {
            return null;
        }

        if (value == null)
        {
            return null;
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
    ///     Gets the value of an attribute when the value is non-nullable
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

        if (typeof(TValue) == typeof(TimeSpan) && TryCoerceTimeSpan(value, out var timeSpan))
        {
            return (TValue)(object)timeSpan;
        }

        return (TValue)Convert.ChangeType(value, typeof(TValue));
    }

    /// <summary>
    ///     Gets the byte array value of an attribute when the value is nullable
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <returns></returns>
    public byte[]? GetAttributeBytesValueOrDefault(string attributeName)
    {
        if (!Attributes.TryGetValue(attributeName, out var value))
        {
            return null;
        }

        if (value == null)
        {
            return null;
        }

        if (value is byte[] b)
        {
            return b;
        }

        throw InvalidAttributeValueException.InvalidDataType(GetLocation(), attributeName, value.GetType(),
            typeof(byte[]));
    }

    /// <summary>
    ///     Gets the byte array value of an attribute when the value is non-nullable
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <returns></returns>
    public byte[] GetAttributeBytesValue(string attributeName)
    {
        if (!Attributes.TryGetValue(attributeName, out var value))
        {
            throw InvalidAttributeValueException.AttributeDoesNotExist(GetLocation(), attributeName);
        }

        if (value == null)
        {
            throw InvalidAttributeValueException.CannotBeNull(GetLocation(), attributeName);
        }

        if (value is byte[] b)
        {
            return b;
        }

        throw InvalidAttributeValueException.InvalidDataType(GetLocation(), attributeName, value.GetType(),
            typeof(byte[]));
    }

    /// <summary>
    ///     Gets the value of an RtRecord attribute when the value is nullable
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <typeparam name="TValue"></typeparam>
    /// <returns></returns>
    public TValue? GetAttributeGeometryObjectValueOrDefault<TValue>(string attributeName)
        where TValue : class, IGeometryObject
    {
        if (!Attributes.TryGetValue(attributeName, out var value))
        {
            return null;
        }

        if (value == null)
        {
            return null;
        }

        if (value is TValue geometryObject)
        {
            return geometryObject;
        }


        throw InvalidAttributeValueException.InvalidDataType(GetLocation(), attributeName, value.GetType(),
            typeof(TValue));
    }

    /// <summary>
    ///     Gets the value of a linked binary attribute when the value is nullable
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <returns></returns>
    public EntityBinaryInfo? GetAttributeLinkedBinaryValueOrDefault(string attributeName)
    {
        if (!Attributes.TryGetValue(attributeName, out var value))
        {
            return null;
        }

        if (value == null)
        {
            return null;
        }

        if (value is EntityBinaryInfo binaryInfo)
        {
            return binaryInfo;
        }


        throw InvalidAttributeValueException.InvalidDataType(GetLocation(), attributeName, value.GetType(),
            typeof(EntityBinaryInfo));
    }

    /// <summary>
    ///     Gets the value of an attribute when the value is non-nullable
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <typeparam name="TValue"></typeparam>
    /// <returns></returns>
    public TValue GetAttributeGeometryObjectValue<TValue>(string attributeName)
        where TValue : class, IGeometryObject
    {
        if (!Attributes.TryGetValue(attributeName, out var value))
        {
            throw InvalidAttributeValueException.CannotBeNull(GetLocation(), attributeName);
        }

        if (value == null)
        {
            throw InvalidAttributeValueException.CannotBeNull(GetLocation(), attributeName);
        }

        if (value is TValue geometryObject)
        {
            return geometryObject;
        }

        throw InvalidAttributeValueException.InvalidDataType(GetLocation(), attributeName, value.GetType(),
            typeof(TValue));
    }

    /// <summary>
    ///     Gets the value of a linked binary attribute when the value is non-nullable
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <returns></returns>
    public EntityBinaryInfo GetAttributeLinkedBinaryValue(string attributeName)
    {
        if (!Attributes.TryGetValue(attributeName, out var value))
        {
            throw InvalidAttributeValueException.CannotBeNull(GetLocation(), attributeName);
        }

        if (value == null)
        {
            throw InvalidAttributeValueException.CannotBeNull(GetLocation(), attributeName);
        }

        if (value is EntityBinaryInfo binaryInfo)
        {
            return binaryInfo;
        }


        throw InvalidAttributeValueException.InvalidDataType(GetLocation(), attributeName, value.GetType(),
            typeof(EntityBinaryInfo));
    }


    /// <summary>
    ///     Gets the value of an attribute if the value is nullable
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <param name="defaultValue"></param>
    /// <returns></returns>
    public object? GetAttributeValueOrDefault(string attributeName, object? defaultValue = null)
    {
        if (!Attributes.TryGetValue(attributeName, out var value))
        {
            return defaultValue;
        }

        return value;
    }

    /// <summary>
    ///     Gets the value of an RtRecord attribute when the value is non-nullable
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
            var rtTypedRecord = (TValue?)Activator.CreateInstance(typeof(TValue), rtRecord);
            if (rtTypedRecord == null)
            {
                throw InvalidAttributeValueException.CannotActivateInstance(typeof(TValue));
            }

            return rtTypedRecord;
        }

        throw InvalidAttributeValueException.InvalidRecordValue(attributeName, typeof(TValue));
    }

    /// <summary>
    ///     Gets the value of an RtRecord attribute when the value is non-nullable
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
            var rtTypedRecord = (TValue?)Activator.CreateInstance(typeof(TValue), rtRecord);
            if (rtTypedRecord == null)
            {
                throw InvalidAttributeValueException.CannotActivateInstance(typeof(TValue));
            }

            return rtTypedRecord;
        }

        throw InvalidAttributeValueException.InvalidRecordValue(attributeName, typeof(TValue));
    }

    /// <summary>
    ///     Gets the value of an attribute if the value is nullable and a string
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <param name="defaultValue"></param>
    /// <returns></returns>
    public string? GetAttributeStringValueOrDefault(string attributeName, string? defaultValue = null)
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
    ///     Gets the value of an attribute in the current object or an embedded document using the given path
    /// </summary>
    /// <param name="ckCacheService">The cache service</param>
    /// <param name="tenantId">Tenant id</param>
    /// <param name="path">Path list to the attribute</param>
    /// <param name="attributeValueResolveFlags">Flags to control how attribute values are resolved</param>
    /// <returns>The value of the attribute or otherwise null</returns>
    public object? GetAttributeValueByAccessPath(ICkCacheService ckCacheService, string tenantId,
        IEnumerable<PathTerm> path,
        AttributeValueResolveFlags attributeValueResolveFlags = AttributeValueResolveFlags.Default)
    {
        return RtPathEvaluator.GetValue(ckCacheService, tenantId, this, path, attributeValueResolveFlags);
    }

    /// <summary>
    ///     Gets the value of an attribute in the current object or an embedded document using the given path
    /// </summary>
    /// <param name="ckCacheService">The cache service</param>
    /// <param name="tenantId">Tenant id</param>
    /// <param name="path">Path list to the attribute</param>
    /// <param name="attributeValueResolveFlags">Flags to control how attribute values are resolved</param>
    /// <returns>The value of the attribute or otherwise null</returns>
    public object? GetAttributeValueByAccessPath(ICkCacheService ckCacheService, string tenantId, string path,
        AttributeValueResolveFlags attributeValueResolveFlags = AttributeValueResolveFlags.Default)
    {
        return RtPathEvaluator.GetValue(ckCacheService, tenantId, this, path, attributeValueResolveFlags);
    }

    /// <summary>
    /// Sets the value of an attribute in the current object or an embedded document using the given path
    /// </summary>
    /// <param name="ckCacheService">The cache service</param>
    /// <param name="tenantId">Tenant id</param>
    /// <param name="path">Path list to the attribute</param>
    /// <param name="value">Value to set</param>
    public void SetAttributeValueByAccessPath(ICkCacheService ckCacheService, string tenantId,
        IEnumerable<PathTerm> path, object? value)
    {
        RtPathEvaluator.SetValue(ckCacheService, tenantId, this, path, value);
    }

    /// <summary>
    /// Sets the value of an attribute in the current object or an embedded document using the given path
    /// </summary>
    /// <param name="ckCacheService">The cache service</param>
    /// <param name="tenantId">Tenant id</param>
    /// <param name="path">Path as string to the attribute</param>
    /// <param name="value">Value to set</param>
    public void SetAttributeValueByAccessPath(ICkCacheService ckCacheService, string tenantId, string path,
        object? value)
    {
        RtPathEvaluator.SetValue(ckCacheService, tenantId, this, path, value);
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
    ///     Sets the value of an attribute when the value is non-nullable
    /// </summary>
    /// <param name="attributeName">The name of the property in PascalCase</param>
    /// <param name="attributeValueTypes">Type of attribute value</param>
    /// <param name="attributeValue">The value of the attribute</param>
    public void SetAttributeValueNonNullable(string attributeName, AttributeValueTypesDto attributeValueTypes,
        object attributeValue)
    {
        _attributes[attributeName] = AttributeValueConverter.ConvertAttributeValue(attributeValueTypes, attributeValue);
    }

    /// <summary>
    ///     Sets an attribute value without any type conversion. Intended for hydration paths
    ///     where the raw value from the data source is assigned directly (e.g. CrateDB row dicts
    ///     being poured into an <see cref="StreamData.StreamDataEntity"/>).
    /// </summary>
    public void SetAttributeRawValue(string attributeName, object? attributeValue)
    {
        _attributes[attributeName] = attributeValue;
    }
}