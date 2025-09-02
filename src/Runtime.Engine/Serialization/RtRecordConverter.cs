using Meshmakers.Common.Shared;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.Runtime.Engine.Serialization;

/// <summary>
/// Converter to handle RtRecords at Yaml Serializer
/// </summary>
internal class RtRecordConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
    {
        if (typeof(Dictionary<object, object>) == type)
        {
            return true;
        }

        return false;
    }

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var rtRecord = new RtRecordTcDto();

        parser.Consume<MappingStart>();

        while (!parser.TryConsume<MappingEnd>(out _))
        {
            var name = parser.Consume<Scalar>();

            if (name.Value == nameof(rtRecord.CkRecordId).ToCamelCase())
            {
                var value = parser.Consume<Scalar>();
                rtRecord.CkRecordId = value.Value;
            }
            else if (name.Value == nameof(rtRecord.Attributes).ToCamelCase())
            {
                parser.Consume<SequenceStart>();

                while (!parser.TryConsume<SequenceEnd>(out _))
                {
                    parser.Consume<MappingStart>();
                    while (!parser.TryConsume<MappingEnd>(out _))
                    {
                        var key = parser.Consume<Scalar>();
                        if (key.Value != nameof(RtAttributeTcDto.Id).ToCamelCase())
                        {
                            throw RuntimeModelParseException.KeyExpectedDuringDeserialization(nameof(RtAttributeTcDto.Id));
                        }

                        var idValue = parser.Consume<Scalar>();
                        key = parser.Consume<Scalar>();
                        if (key.Value != nameof(RtAttributeTcDto.Value).ToCamelCase())
                        {
                            throw RuntimeModelParseException.KeyExpectedDuringDeserialization(nameof(RtAttributeTcDto.Value));
                        }

                        var value = parser.Consume<Scalar>();

                        rtRecord.Attributes.Add(new RtAttributeTcDto
                        {
                            Id = idValue.Value,
                            Value = value.Value
                        });
                    }
                }
            }
        }

        return rtRecord;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer rootSerializer)
    {
        throw RuntimeModelParseException.NotImplemented();
    }
}