using System.Collections;
using System.Globalization;
using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
/// Converts attribute values based their construction kit type.
/// </summary>
public static class AttributeValueConverter
{
    /// <summary>
    /// Gets the .NET type for the given <see cref="AttributeValueTypesDto"/>
    /// </summary>
    /// <param name="attributeValueTypes">The attribute value type</param>
    /// <returns></returns>
    /// <exception cref="NotSupportedException"></exception>
    public static Type GetDotNetType(AttributeValueTypesDto attributeValueTypes)
    {
        switch (attributeValueTypes)
        {
            case AttributeValueTypesDto.String:
                return typeof(string);
            case AttributeValueTypesDto.DateTime:
                return typeof(DateTime);
            case AttributeValueTypesDto.Boolean:
                return typeof(Boolean);
            case AttributeValueTypesDto.DateTimeOffset:
                return typeof(DateTimeOffset);
            case AttributeValueTypesDto.Double:
                return typeof(Double);
            case AttributeValueTypesDto.Enum:
                return typeof(Enum);
            case AttributeValueTypesDto.Int:
                return typeof(Int32);
            case AttributeValueTypesDto.Int64:
                return typeof(Int64);
            case AttributeValueTypesDto.IntArray:
                return typeof(Int32[]);
            case AttributeValueTypesDto.StringArray:
                return typeof(string[]);
            case AttributeValueTypesDto.TimeSpan:
                return typeof(TimeSpan);
            default:
                throw new NotSupportedException($"AttributeValueTypesDto '{attributeValueTypes}' is not supported.");
        }
    }

    /// <summary>
    /// Converts the given value to the given <see cref="AttributeValueTypesDto"/>
    /// </summary>
    /// <param name="attributeValueTypes">The attribute value type</param>
    /// <param name="value">The value to convert</param>
    /// <returns>Converted value</returns>
    public static object? ConvertAttributeValue(AttributeValueTypesDto attributeValueTypes, object? value)
    {
        if (value == null)
        {
            return null;
        }

        switch (attributeValueTypes)
        {
            case AttributeValueTypesDto.StringArray:
                if (value is string[] stringArray)
                {
                    return stringArray.ToList();
                }

                if (value is List<object> objectList)
                {
                    return objectList.Select(x =>
                    {
                        if (x is JsonElement jsonElement)
                        {
                            return jsonElement.GetString();
                        }

                        return Convert.ToString(x);
                    }).ToList();
                }

                if (value is AttributeStringValueList stringList)
                {
                    return stringList.InnerList;
                }

                break;
            case AttributeValueTypesDto.String:
                if (value is string)
                {
                    return value;
                }

                return value.ToString();
            case AttributeValueTypesDto.Double:
                if (value is double)
                {
                    return value;
                }

                if (double.TryParse(value.ToString(), NumberStyles.Float, new CultureInfo("en-US"), out var doubleResult))
                {
                    return doubleResult;
                }

                break;
            case AttributeValueTypesDto.Boolean:
                if (value is bool)
                {
                    return value;
                }

                if (bool.TryParse(value.ToString(), out var boolResult))
                {
                    return boolResult;
                }

                break;
            case AttributeValueTypesDto.IntArray:
                if (value is int[] intArray)
                {
                    return intArray.ToList();
                }

                if (value is List<object> objectListInt)
                {
                    return objectListInt.Select(x =>
                    {
                        if (x is JsonElement jsonElement)
                        {
                            return Convert.ToInt32(jsonElement.GetString());
                        }

                        return Convert.ToInt32(x);
                    }).ToList();
                }

                if (value is AttributePrimitiveValueList<int> intList)
                {
                    return intList.InnerList;
                }

                break;
            case AttributeValueTypesDto.Int:
            case AttributeValueTypesDto.Enum:
                if (value is int)
                {
                    return value;
                }

                if (value is JsonElement i32Element)
                {
                    if (i32Element.ValueKind == JsonValueKind.Number)
                    {
                        return i32Element.GetInt32();
                    }
                    
                    if (i32Element.ValueKind == JsonValueKind.String)
                    {
                        if (int.TryParse(i32Element.GetString(), out var svi32))
                        {
                            return svi32;
                        }
                    }
                }

                if (int.TryParse(value.ToString(), out var intResult))
                {
                    return intResult;
                }

                break;
            case AttributeValueTypesDto.Int64:
                if (value is int)
                {
                    return value;
                }

                if (value is JsonElement i64Element)
                {
                    return i64Element.GetInt64();
                }

                if (long.TryParse(value.ToString(), out var longResult))
                {
                    return longResult;
                }

                break;
            case AttributeValueTypesDto.DateTime:
                value = Convert.ToDateTime(value);
                break;
            case AttributeValueTypesDto.TimeSpan:
                if (value is TimeSpan)
                {
                    return value;
                }

                if (TimeSpan.TryParse(value.ToString(), out var timeSpanResult))
                {
                    return timeSpanResult;
                }

                break;
            case AttributeValueTypesDto.DateTimeOffset:
                if (value is DateTimeOffset)
                {
                    return value;
                }

                if (DateTimeOffset.TryParse(value.ToString(), out var dateTimeOffsetResult))
                {
                    return dateTimeOffsetResult;
                }

                break;
            case AttributeValueTypesDto.RecordArray:
                if (value is RtRecord[] recordArray)
                {
                    return recordArray.ToList();
                }

                if (value is IAttributeRecordValueArray recordList)
                {
                    return recordList.RecordList;
                }

                if (value is IEnumerable recordObjectList)
                {
                    return recordObjectList.Cast<RtRecord>().ToList();
                }

                break;
            case AttributeValueTypesDto.Record:
                if (value.GetType() == typeof(RtRecord))
                {
                    return value;
                }

                RtRecord rtRecord = (RtRecord)value;

                return new RtRecord(rtRecord.CkRecordId,
                    rtRecord.Attributes.ToDictionary(k => k.Key, v => v.Value));
        }

        return value;
    }
}