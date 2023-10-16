using System.Globalization;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
/// Converts attribute values based their construction kit type.
/// </summary>
internal static class AttributeValueConverter
{
    internal static Type GetDotNetType(AttributeValueTypesDto attributeValueTypes)
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
    
    internal static object? ConvertAttributeValue(AttributeValueTypesDto attributeValueTypes, object? value)
    {
        if (value == null)
        {
            return null;
        }

        switch (attributeValueTypes)
        {
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
            case AttributeValueTypesDto.Int:
                if (value is int)
                {
                    return value;
                }

                if (int.TryParse(value.ToString(), out var intResult))
                {
                    return intResult;
                }

                break;
            case AttributeValueTypesDto.DateTime:
                value = Convert.ToDateTime(value);
                break;
        }

        return value;
    }
}