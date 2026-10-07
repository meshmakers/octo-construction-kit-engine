using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.Runtime.Engine.Serialization;

/// <summary>
///     YAML counterpart of <see cref="RtSecretValueJsonConverter" /> (AB#5532): an
///     <see cref="RtSecretValue" /> is written as the marker <c>{isSet: true|false}</c>, never as its
///     envelope or plaintext; a scalar reads as <see cref="RtSecretValue.Pending" />, the marker mapping
///     (only <c>isSet</c> / <c>keyMissing</c> booleans and <c>setAt</c> date or null, possibly empty) as
///     <c>Pending("")</c> ("unchanged"); any other node throws a
///     <see cref="YamlException" /> without the value (strict contract of <see cref="RtSecretValueWireFormat" />).
/// </summary>
internal sealed class RtSecretValueYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
    {
        return type == typeof(RtSecretValue);
    }

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        if (parser.TryConsume<Scalar>(out var scalar))
        {
            // YAML scalars carry no string/number distinction without tags: any non-null scalar is input.
            return scalar.Value is "~" or "null" && scalar.Style == ScalarStyle.Plain
                ? null
                : RtSecretValue.Pending(scalar.Value);
        }

        if (parser.TryConsume<MappingStart>(out var mappingStart))
        {
            // Strict marker: only isSet / keyMissing (boolean scalars) and setAt (date or null), possibly
            // empty (RtSecretValueWireFormat).
            while (!parser.TryConsume<MappingEnd>(out _))
            {
                if (!parser.TryConsume<Scalar>(out var key) || !RtSecretValueWireFormat.IsMarkerProperty(key.Value))
                {
                    throw new YamlException(mappingStart.Start, mappingStart.End,
                        "A Secret value must be a scalar (the secret to store), null (clear) or the marker " +
                        "{isSet: true|false} (unchanged); the mapping has other keys.");
                }

                var isDate = string.Equals(key.Value, RtSecretValueWireFormat.SetAtPropertyName,
                    StringComparison.OrdinalIgnoreCase);
                if (!parser.TryConsume<Scalar>(out var flag) ||
                    !RtSecretValueWireFormat.IsValidMarkerText(key.Value, ToMarkerText(flag, isDate)))
                {
                    throw new YamlException(mappingStart.Start, mappingStart.End,
                        isDate
                            ? $"The key '{RtSecretValueWireFormat.SetAtPropertyName}' of a Secret marker must be a date or null."
                            : $"The keys '{RtSecretValueWireFormat.IsSetPropertyName}' and '{RtSecretValueWireFormat.KeyMissingPropertyName}' of a Secret marker must be booleans.");
                }
            }

            return RtSecretValue.Pending(string.Empty);
        }

        var current = parser.Current;
        throw new YamlException(current?.Start ?? Mark.Empty, current?.End ?? Mark.Empty,
            "A Secret value must be a scalar (the secret to store), null (clear) or the marker " +
            "{isSet: true|false} (unchanged); got a sequence or another node.");
    }

    /// <summary>
    ///     Text of a marker scalar for <see cref="RtSecretValueWireFormat.IsValidMarkerText" />: booleans must be
    ///     plain scalars (a quoted "true" is a string); a plain null scalar is <c>null</c> for <c>setAt</c>.
    /// </summary>
    private static string? ToMarkerText(Scalar scalar, bool isDate)
    {
        if (scalar.Style != ScalarStyle.Plain)
        {
            return isDate ? scalar.Value : string.Empty;
        }

        return isDate && scalar.Value is "" or "~" or "null" or "Null" or "NULL" ? null : scalar.Value;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        if (value is not RtSecretValue secret)
        {
            emitter.Emit(new Scalar("null"));
            return;
        }

        emitter.Emit(new MappingStart());
        emitter.Emit(new Scalar(RtSecretValueWireFormat.IsSetPropertyName));
        emitter.Emit(new Scalar(RtSecretValueWireFormat.IsSet(secret) ? "true" : "false"));
        emitter.Emit(new MappingEnd());
    }
}
