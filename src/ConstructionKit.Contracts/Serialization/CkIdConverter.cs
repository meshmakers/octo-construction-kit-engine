using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

public class CkIdAttributeIdConverter : CkIdConverter<CkAttributeId>
{
}

public class CkIdTypeIdConverter : CkIdConverter<CkTypeId>
{
}

public class CkIdAssociationIdConverter : CkIdConverter<CkAssociationRoleId>
{
}

public class CkIdConverter<TKey> : JsonConverter<CkId<TKey>>, IYamlTypeConverter where TKey : struct, IComparable<TKey>, ICkKey
{
    public override CkId<TKey> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.TokenType == JsonTokenType.String
            ? reader.GetString()
            : throw ModelParseException.UnexpectedToken(nameof(CkModelId), reader.TokenType);
        return !string.IsNullOrEmpty(str) ? new CkId<TKey>(str) : throw ModelParseException.ValueCannotBeEmpty(nameof(CkModelId));
    }

    public override void Write(Utf8JsonWriter writer, CkId<TKey> value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }

    public bool Accepts(Type type)
    {
        return type == typeof(CkId<TKey>);
    }

    public object? ReadYaml(IParser parser, Type type)
    {
        var value = parser.Consume<Scalar>().Value;
        return new CkId<TKey>(value); 
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type)
    {
        var ckId = (CkId<TKey>)value!;
        emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, ckId.SemanticVersionedFullName, ScalarStyle.Any, true, false));
    }
}