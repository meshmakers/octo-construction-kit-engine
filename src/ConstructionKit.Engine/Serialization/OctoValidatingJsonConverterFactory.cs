using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Json.Schema;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Engine.Serialization;

internal class OctoValidatingJsonConverterFactory : JsonConverterFactory
{
    private readonly ConcurrentDictionary<Type, JsonConverter?> _cache = new();

    /// <summary>Specifies the output format.</summary>
    public OutputFormat? OutputFormat { get; init; }

    /// <summary>
    /// Specifies whether the `format` keyword should be required to provide
    /// validation results.  Default is false, which just produces annotations
    /// for drafts 2019-09 and prior or follows the behavior set forth by the
    /// format-annotation vocabulary requirement in the `$vocabulary` keyword in
    /// a meta-schema declaring draft 2020-12.
    /// </summary>
    public bool? RequireFormatValidation { get; init; }

    /// <summary>When overridden in a derived class, determines whether the converter instance can convert the specified object type.</summary>
    /// <param name="typeToConvert">The type of the object to check whether it can be converted by this converter instance.</param>
    /// <returns>
    /// <see langword="true" /> if the instance can convert the specified object type; otherwise, <see langword="false" />.</returns>
    public override bool CanConvert(Type typeToConvert)
    {
        if (_cache.TryGetValue(typeToConvert, out var jsonConverter))
            return jsonConverter != null;
        var isSchemaExisting = typeToConvert.GetCustomAttributes(typeof(OctoJsonSchemaAttribute)).SingleOrDefault() != null;
        if (isSchemaExisting)
        {
            return true;
        }

        _cache[typeToConvert] = null;
        return isSchemaExisting;
    }

    /// <summary>Creates a converter for a specified type.</summary>
    /// <param name="typeToConvert">The type handled by the converter.</param>
    /// <param name="options">The serialization options to use.</param>
    /// <returns>
    /// An instance of a <see cref="T:System.Text.Json.Serialization.JsonConverter`1" /> where `T` is compatible with <paramref name="typeToConvert" />.
    /// If <see langword="null" /> is returned, a <see cref="T:System.NotSupportedException" /> will be thrown.
    /// </returns>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (_cache.TryGetValue(typeToConvert, out var converter1))
        {
            return converter1;
        }

        OctoJsonSchemaAttribute jsonSchemaAttribute =
            (OctoJsonSchemaAttribute)typeToConvert.GetCustomAttributes(typeof(OctoJsonSchemaAttribute)).Single();
        Type type = typeof(OctoValidatingJsonConverter<>).MakeGenericType(typeToConvert);
        
        Func<JsonSerializerOptions, JsonSerializerOptions> func = o =>
        {
            JsonSerializerOptions serializerOptions = new JsonSerializerOptions(o);
            serializerOptions.Converters.Remove(this);
            return serializerOptions;
        };
        object[] objArray =
        {
            jsonSchemaAttribute.Schema,
            func
        };
        
        var instance = (JsonConverter?)Activator.CreateInstance(type, objArray);
        var validatingJsonConverter = (IOctoValidatingJsonConverter?)instance;
        if (validatingJsonConverter != null)
        {
            validatingJsonConverter.OutputFormat = OutputFormat.GetValueOrDefault();
            validatingJsonConverter.RequireFormatValidation = RequireFormatValidation.GetValueOrDefault();
            _cache[typeToConvert] = instance;
        }

        return instance;
    }
}