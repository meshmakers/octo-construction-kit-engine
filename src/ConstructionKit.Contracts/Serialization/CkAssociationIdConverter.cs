using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
/// Converter for System.Text.Json and YamlDotNet for <see cref="CkAssociationRoleId"/>
/// </summary>
public class CkAssociationIdConverter : JsonConverter<CkAssociationRoleId>, IYamlTypeConverter
{
    /// <inheritdoc />
    public override CkAssociationRoleId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.TokenType == JsonTokenType.String
            ? reader.GetString()
            : throw ModelParseException.UnexpectedToken(nameof(CkAssociationRoleId), reader.TokenType);
        return !string.IsNullOrEmpty(str) && str != null ? new CkAssociationRoleId(str) : throw ModelParseException.ValueCannotBeEmpty(nameof(CkAssociationRoleId));
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, CkAssociationRoleId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    public bool Accepts(Type type)
    {
        return type == typeof(CkAssociationRoleId);
    }

    /// <inheritdoc />
    public object ReadYaml(IParser parser, Type type)
    {
        var value = parser.Consume<Scalar>().Value;
        return new CkAssociationRoleId(value); 
    }

    /// <inheritdoc />
    public void WriteYaml(IEmitter emitter, object? value, Type type)
    {
        var ckAssociationRoleId = (CkAssociationRoleId)value!;
        emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, ckAssociationRoleId.SemanticVersionedFullName, ScalarStyle.Any, true, false));
    }
}
