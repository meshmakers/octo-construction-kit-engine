using Meshmakers.Common.Shared;
using Meshmakers.Octo.Runtime.Contracts.DataTransferObjects;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.Runtime.Engine.Serialization;

/// <summary>
/// 
/// </summary>
internal class TestConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
    {
        System.Diagnostics.Debug.WriteLine(type.FullName);
        if (typeof(Dictionary<object, object>) == type)
        {
            return true;
        }

        return false;
    }

    public object? ReadYaml(IParser parser, Type type)
    {
        var rtRecord = new RtRecordDto();

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
                        if (key.Value != nameof(RtAttributeDto.Id).ToCamelCase())
                        {
                            throw new YamlException("Expected Id");
                        }

                        var idValue = parser.Consume<Scalar>();
                        key = parser.Consume<Scalar>();
                        if (key.Value != nameof(RtAttributeDto.Value).ToCamelCase())
                        {
                            throw new YamlException("Expected Value");
                        }

                        var value = parser.Consume<Scalar>();

                        rtRecord.Attributes.Add(new RtAttributeDto
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


    public void WriteYaml(IEmitter emitter, object? value, Type type)
    {
    }
}