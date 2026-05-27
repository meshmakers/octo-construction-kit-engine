using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.Serialization;

/// <summary>
///     YAML round-trip converter for <see cref="RequiresMap"/>. Allows the value of each
///     entry in the <c>requires:</c> block to be written as either a scalar shortcut or a
///     YAML sequence — both are normalised to <see cref="List{T}"/> on read. Serialisation
///     always emits sequences for round-trip stability.
/// </summary>
public sealed class RequiresMapConverter : IYamlTypeConverter
{
    /// <inheritdoc />
    public bool Accepts(Type type) => type == typeof(RequiresMap);

    /// <inheritdoc />
    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var result = new RequiresMap();

        parser.Consume<MappingStart>();
        while (!parser.TryConsume<MappingEnd>(out _))
        {
            var key = parser.Consume<Scalar>().Value;
            var values = new List<string>();

            if (parser.TryConsume<Scalar>(out var scalar))
            {
                // octo.isSystemTenant: "true"   →   ["true"]
                values.Add(scalar.Value);
            }
            else if (parser.TryConsume<SequenceStart>(out _))
            {
                // octo.environment:             →   ["staging", "production"]
                //   - staging
                //   - production
                while (!parser.TryConsume<SequenceEnd>(out _))
                {
                    var item = parser.Consume<Scalar>();
                    values.Add(item.Value);
                }
            }
            else
            {
                throw new YamlException(
                    $"Unsupported value shape for requires key '{key}'. Expected scalar or sequence of strings.");
            }

            result[key] = values;
        }

        return result;
    }

    /// <inheritdoc />
    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer rootSerializer)
    {
        var map = (RequiresMap)value!;

        emitter.Emit(new MappingStart());
        foreach (var entry in map)
        {
            emitter.Emit(new Scalar(entry.Key));
            emitter.Emit(new SequenceStart(AnchorName.Empty, TagName.Empty, true, SequenceStyle.Block));
            foreach (var item in entry.Value)
            {
                emitter.Emit(new Scalar(item));
            }
            emitter.Emit(new SequenceEnd());
        }
        emitter.Emit(new MappingEnd());
    }
}
