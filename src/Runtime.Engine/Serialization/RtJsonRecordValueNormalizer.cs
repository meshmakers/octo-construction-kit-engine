using Meshmakers.Common.Shared;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Newtonsoft.Json.Linq;

namespace Meshmakers.Octo.Runtime.Engine.Serialization;

/// <summary>
///     AB#8325: the JSON entity stream reads an attribute value (<see cref="RtAttributeTcDto.Value" />, type
///     <c>object</c>) as a <see cref="JObject" /> or <see cref="JArray" /> when it is not a scalar. The import expects a
///     record as <see cref="RtRecordTcDto" /> (as the YAML reader produces it), so a record value in a JSON file was dropped
///     (single record) or failed the import with an <see cref="InvalidCastException" /> (record array). This turns record
///     values into <see cref="RtRecordTcDto" />, nested records included. Every other value is left as it is.
/// </summary>
internal static class RtJsonRecordValueNormalizer
{
    private static readonly string CkRecordIdName = nameof(RtRecordTcDto.CkRecordId).ToCamelCase();
    private static readonly string AttributesName = nameof(RtRecordTcDto.Attributes).ToCamelCase();
    private static readonly string IdName = nameof(RtAttributeTcDto.Id).ToCamelCase();
    private static readonly string ValueName = nameof(RtAttributeTcDto.Value).ToCamelCase();

    /// <summary>Converts the record values of the entity's attributes in place.</summary>
    public static void Normalize(RtEntityTcDto entity)
    {
        foreach (var attribute in entity.Attributes)
        {
            attribute.Value = NormalizeValue(attribute.Value);
        }
    }

    private static object? NormalizeValue(object? value)
    {
        switch (value)
        {
            case JObject jObject when IsRecord(jObject):
                return ToRecord(jObject);
            case JArray jArray when jArray.Count > 0 && jArray.All(e => e is JObject o && IsRecord(o)):
                return jArray.Select(e => (object)ToRecord((JObject)e)).ToList();
            default:
                return value;
        }
    }

    private static bool IsRecord(JObject jObject) => jObject.ContainsKey(CkRecordIdName);

    private static RtRecordTcDto ToRecord(JObject jObject)
    {
        var record = new RtRecordTcDto { CkRecordId = jObject.Value<string>(CkRecordIdName)! };
        if (jObject[AttributesName] is not JArray attributes)
        {
            return record;
        }

        foreach (var attribute in attributes.OfType<JObject>())
        {
            var valueToken = attribute[ValueName];
            record.Attributes.Add(new RtAttributeTcDto
            {
                Id = attribute.Value<string>(IdName)!,
                Value = NormalizeValue(valueToken switch
                {
                    null or { Type: JTokenType.Null } => null,
                    JValue jValue => jValue.Value,
                    _ => valueToken
                })
            });
        }

        return record;
    }
}
