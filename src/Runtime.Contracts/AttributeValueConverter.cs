using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;

namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
/// Converts attribute values based their construction kit type.
/// </summary>
internal static class AttributeValueConverter
{
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

                if (double.TryParse(value.ToString(), out var doubleResult))
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