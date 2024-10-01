using System.ComponentModel;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
/// Converter for <see cref="RtEntityId"/> to JSON using System.Text.Json
/// </summary>
public class RtEntityIdConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    }

    /// <inheritdoc />
    public override object? ConvertFrom(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture,
        object value)
    {
        if (value is string stringValue)
        {
            string decodedValue = Uri.UnescapeDataString(stringValue);
            return new RtEntityId(decodedValue);
        }

        return base.ConvertFrom(context, culture, value);
    }
}