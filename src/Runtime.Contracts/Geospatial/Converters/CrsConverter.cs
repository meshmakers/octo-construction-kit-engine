using System.Text.Json;
using System.Text.Json.Serialization;
using Meshmakers.Common.Shared;
using Meshmakers.Octo.Runtime.Contracts.Geospatial.CoordinateReferenceSystem;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.Runtime.Contracts.Geospatial.Converters;

/// <summary>
/// Converter for System.Text.Json and YamlDotNet for <see cref="ICRSObject" />
/// </summary>
public class CrsConverter: JsonConverter<ICRSObject>, IYamlTypeConverter
{
    /// <inheritdoc />
    public override ICRSObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return new UnspecifiedCRS();
        }

        using var jsonDocument = JsonDocument.ParseValue(ref reader);
        {
            var jsonObject = jsonDocument.RootElement;

            if (!jsonObject.TryGetProperty("type", out var typeElement))
            {
                throw RuntimeModelParseException.MissingProperty(nameof(ICRSObject), "type");
            }

            var crsType = typeElement.GetString();

            if (string.Equals("name", crsType, StringComparison.OrdinalIgnoreCase))
            {
                if (jsonObject.TryGetProperty("properties", out var properties) && properties.TryGetProperty("name", out var nameValue))
                {
                    return new NamedCRS(nameValue.GetString());
                }
            }
            else if (string.Equals("link", crsType, StringComparison.OrdinalIgnoreCase))
            {
                if (jsonObject.TryGetProperty("properties", out var properties) && properties.TryGetProperty("href", out var hrefValue))
                {
                    return new LinkedCRS(hrefValue.GetString());
                }
            }

            throw RuntimeModelParseException.InvalidType(crsType);
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ICRSObject? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        switch (value.Type)
        {
            case CRSType.Name:
            case CRSType.Link:
                JsonSerializer.Serialize(writer, value, options);
                break;
            case CRSType.Unspecified:
                writer.WriteNullValue();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value.Type), $"Unexpected CRSType value: {value.Type}");
        }
    }

    /// <inheritdoc />
    public bool Accepts(Type type)
    {
        return typeof(ICRSObject).IsAssignableFrom(type);
    }

    /// <inheritdoc />
    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        Dictionary<string, Scalar> properties = new();
        string? crsType = null;
        
        parser.Consume<MappingStart>();
        while (!parser.TryConsume<MappingEnd>(out _))
        {
            var propertyName = parser.Consume<Scalar>();
            if (propertyName.Value == "type")
            {
                crsType = parser.Consume<Scalar>().Value;
            }
            else if (propertyName.Value == "properties")
            {
                parser.Consume<MappingStart>();
                
                while (!parser.TryConsume<MappingEnd>(out _))
                {
                    var propertyKey = parser.Consume<Scalar>();
                    var propertyValue = parser.Consume<Scalar>();
                    properties.Add(propertyKey.Value, propertyValue);
                }
            }
            else
            {
                throw RuntimeModelParseException.UnexpectedFormat(propertyName.Value);
            }
        }
        
        if (string.IsNullOrWhiteSpace(crsType))
        {
            throw RuntimeModelParseException.MissingProperty(nameof(ICRSObject), "type");
        }
        
        if (string.Equals("name", crsType, StringComparison.OrdinalIgnoreCase))
        {
            if (properties.TryGetValue("name", out var nameValue))
            {
                return new NamedCRS(nameValue.Value);
            }
        }
        else if (string.Equals("link", crsType, StringComparison.OrdinalIgnoreCase))
        {
            if (properties.TryGetValue("href", out var hrefValue))
            {
                return new LinkedCRS(hrefValue.Value);
            }
        }
      
        return new UnspecifiedCRS();
    }

    /// <inheritdoc />
    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer rootSerializer)
    {
        if (value == null)
        {
            emitter.Emit(new Scalar(null, null, "null", ScalarStyle.Plain, true, false));
            return;
        }

        if (value is ICRSObject crsConverter)
        {
            switch (crsConverter.Type)
            {
                case CRSType.Name:
                    emitter.Emit(new MappingStart(null, null, false, MappingStyle.Block));
                    emitter.Emit(new Scalar(null, null, "type", ScalarStyle.Plain, true, false));
                    emitter.Emit(new Scalar(null, null, "name", ScalarStyle.Plain, true, false));
                    emitter.Emit(new Scalar(null, null, "properties", ScalarStyle.Plain, true, false));
                    emitter.Emit(new Scalar(null, null, "name", ScalarStyle.Plain, true, false));
                    emitter.Emit(new Scalar(null, null, crsConverter.Properties["name"]?.ToString() ?? "null", ScalarStyle.Plain, true, false));
                    emitter.Emit(new MappingEnd());
                    break;
                case CRSType.Link:
                    emitter.Emit(new MappingStart(null, null, false, MappingStyle.Block));
                    emitter.Emit(new Scalar(null, null, "type", ScalarStyle.Plain, true, false));
                    emitter.Emit(new Scalar(null, null, "link", ScalarStyle.Plain, true, false));
                    emitter.Emit(new Scalar(null, null, "properties", ScalarStyle.Plain, true, false));
                    emitter.Emit(new Scalar(null, null, "href", ScalarStyle.Plain, true, false));
                    emitter.Emit(new Scalar(null, null, crsConverter.Properties["href"]?.ToString() ?? "null", ScalarStyle.Plain, true, false));
                    emitter.Emit(new MappingEnd());
                    break;
                case CRSType.Unspecified:
                    emitter.Emit(new Scalar(null, null, "null", ScalarStyle.Plain, true, false));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(crsConverter.Type), $"Unexpected CRSType value: {crsConverter.Type}");
            }
        }
    }
}