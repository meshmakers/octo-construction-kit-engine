using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
/// Converter for System.Text.Json and YamlDotNet for <see cref="ICollection{Object}"/>/>
/// </summary>
public class ObjectCollectionConverter : IYamlTypeConverter
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public bool Accepts(Type type)
    {
        System.Diagnostics.Debug.WriteLine(type);
        var r =  type.IsAssignableFrom(typeof(ICollection<object>));
        System.Diagnostics.Debug.WriteLine($"result: {r}");

        return r;
    }

    /// <inheritdoc />
    public object? ReadYaml(IParser parser, Type type)
    {
        var listType = typeof(List<object>);
        var list = (IList<object>)Activator.CreateInstance(listType)!;
        
        parser.Consume<SequenceStart>();

        while (!parser.TryConsume<SequenceEnd>(out _))
        {
            var scalar = parser.Consume<Scalar>();
            if (!scalar.IsKey)
            {
                list.Add(scalar.Value);
            }
        }

        return list;
    }

    /// <inheritdoc />
    public void WriteYaml(IEmitter emitter, object? value, Type type)
    {
        emitter.Emit(new SequenceStart(null, null, false, SequenceStyle.Block));

        if (value is ICollection<object> values)
        {
            foreach (var v in values)
            {
                var s = v.ToString();
                if (s != null)
                {
                    emitter.Emit(new Scalar(s));
                }
            }
        }
        
        emitter.Emit(new SequenceEnd());
    }
}