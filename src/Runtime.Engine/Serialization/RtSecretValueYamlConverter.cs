using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.Runtime.Engine.Serialization;

/// <summary>
///     YAML counterpart of <see cref="RtSecretValueJsonConverter" /> (AB#5532): an
///     <see cref="RtSecretValue" /> is written as the marker <c>{isSet: true|false}</c>, never as its
///     envelope or plaintext; a scalar reads as <see cref="RtSecretValue.Pending" />, a mapping as
///     <c>Pending("")</c> ("unchanged").
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
            return scalar.Value is "~" or "null" && scalar.Style == ScalarStyle.Plain
                ? null
                : RtSecretValue.Pending(scalar.Value);
        }

        parser.SkipThisAndNestedEvents();
        return RtSecretValue.Pending(string.Empty);
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
