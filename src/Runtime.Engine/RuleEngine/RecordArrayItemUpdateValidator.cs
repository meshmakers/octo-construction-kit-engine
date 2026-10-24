using System.Globalization;
using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Messages;
using Newtonsoft.Json.Linq;

namespace Meshmakers.Octo.Runtime.Engine.RuleEngine;

/// <summary>
///     AB#7445: validates and normalises the <see cref="IEntityUpdateInfo{TEntity}.RecordArrayItemUpdates" /> of an
///     update. The item update is written without reading the stored entity, so everything that could be checked
///     against the stored value is checked against the Construction Kit instead: the attribute must be a
///     <c>RecordArray</c>, the members must exist in its record, must not be <c>Secret</c> and the values must have
///     the member's value type.
/// </summary>
internal static class RecordArrayItemUpdateValidator
{
    /// <summary>
    ///     Value types a member of an item update may have (scalar values; no arrays, records, points, binaries).
    /// </summary>
    private static readonly HashSet<AttributeValueTypesDto> SupportedMemberTypes =
    [
        AttributeValueTypesDto.String, AttributeValueTypesDto.Integer, AttributeValueTypesDto.Integer64,
        AttributeValueTypesDto.Double, AttributeValueTypesDto.Boolean, AttributeValueTypesDto.DateTime,
        AttributeValueTypesDto.DateTimeOffset, AttributeValueTypesDto.TimeSpan, AttributeValueTypesDto.Enum
    ];

    /// <summary>
    ///     Value types of a match member (an item identity).
    /// </summary>
    private static readonly HashSet<AttributeValueTypesDto> SupportedMatchTypes =
    [
        AttributeValueTypesDto.String, AttributeValueTypesDto.Integer, AttributeValueTypesDto.Integer64,
        AttributeValueTypesDto.Enum
    ];

    /// <summary>
    ///     Validates the item updates of <paramref name="info" />. Returns the normalised updates (values converted
    ///     to the CLR type of the member, entries targeting the same item merged), or <c>null</c> when an error was
    ///     added to <paramref name="operationResult" />.
    /// </summary>
    public static IReadOnlyList<RtRecordArrayItemUpdate>? Validate<TEntity>(ICkCacheService ckCache,
        string tenantId, CkTypeGraph ckTypeGraph, IEntityUpdateInfo<TEntity> info,
        IOriginFileResolver originFileResolver, OperationResult operationResult) where TEntity : RtEntity
    {
        var itemUpdates = info.RecordArrayItemUpdates;
        if (itemUpdates == null || itemUpdates.Count == 0)
        {
            return null;
        }

        var location = originFileResolver.Resolve(tenantId);
        object rtId = (object?)info.RtId ?? (object?)info.RtEntity?.RtId ?? "?";
        var isInError = false;

        void Fail(string attributeName, string memberName, string reason)
        {
            operationResult.AddMessage(MessageCodes.RecordArrayItemUpdateInvalid(location, tenantId,
                info.CkTypeId, rtId, attributeName, memberName, reason));
            isInError = true;
        }

        if (info.ModOption != EntityModOptions.Update)
        {
            Fail("-", "-", $"record array item updates are only supported with mod option Update, not '{info.ModOption}'");
            return null;
        }

        var merged = new Dictionary<(string Attribute, string MatchMember, string MatchValue), MergedItem>();
        foreach (var itemUpdate in itemUpdates)
        {
            var attributeName = itemUpdate.AttributeName;
            if (!ckTypeGraph.AllAttributesByName.TryGetValue(attributeName, out var attribute) ||
                attribute.ValueType != AttributeValueTypesDto.RecordArray || attribute.ValueCkRecordId == null)
            {
                Fail(attributeName, itemUpdate.MatchMember,
                    "the attribute is not a RecordArray attribute of the type");
                continue;
            }

            if (info.RtEntity != null && info.RtEntity.Attributes.ContainsKey(attributeName))
            {
                Fail(attributeName, itemUpdate.MatchMember,
                    "the entity update also carries a whole value for this RecordArray, which is ambiguous with an item update");
                continue;
            }

            if (!ckCache.TryGetCkRecord(tenantId, attribute.ValueCkRecordId, out var recordGraph))
            {
                operationResult.AddMessage(MessageCodes.CkRecordIdNotFound(location, tenantId,
                    attribute.ValueCkRecordId));
                isInError = true;
                continue;
            }

            var matchMember = ResolveMember(ckCache, tenantId, recordGraph, itemUpdate.MatchMember);
            if (matchMember == null)
            {
                Fail(attributeName, itemUpdate.MatchMember, "the match member does not exist in the record");
                continue;
            }

            if (!SupportedMatchTypes.Contains(matchMember.ValueType))
            {
                Fail(attributeName, itemUpdate.MatchMember,
                    $"the match member has value type '{matchMember.ValueType}', supported are String, Integer, Integer64 and Enum");
                continue;
            }

            if (!TryConvert(matchMember.ValueType, itemUpdate.MatchValue, out var matchValue) || matchValue == null)
            {
                Fail(attributeName, itemUpdate.MatchMember,
                    $"the match value is not a valid '{matchMember.ValueType}' value");
                continue;
            }

            if (itemUpdate.MemberUpdates.Count == 0)
            {
                Fail(attributeName, itemUpdate.MatchMember, "the item update has no member to write");
                continue;
            }

            var key = (attributeName, matchMember.AttributeName,
                Convert.ToString(matchValue, CultureInfo.InvariantCulture) ?? string.Empty);
            if (!merged.TryGetValue(key, out var mergedItem))
            {
                mergedItem = new MergedItem(attributeName, matchMember.AttributeName, matchValue);
                merged.Add(key, mergedItem);
            }

            foreach (var (memberName, rawValue) in itemUpdate.MemberUpdates)
            {
                var member = ResolveMember(ckCache, tenantId, recordGraph, memberName);
                if (member == null)
                {
                    Fail(attributeName, memberName, "the member does not exist in the record");
                    continue;
                }

                if (member.ValueType == AttributeValueTypesDto.Secret)
                {
                    operationResult.AddMessage(MessageCodes.RecordArrayItemUpdateSecretMember(location, tenantId,
                        info.CkTypeId, rtId, attributeName, memberName));
                    isInError = true;
                    continue;
                }

                if (string.Equals(member.AttributeName, matchMember.AttributeName, StringComparison.Ordinal))
                {
                    Fail(attributeName, memberName, "the match member identifies the item and cannot be written");
                    continue;
                }

                if (!SupportedMemberTypes.Contains(member.ValueType))
                {
                    Fail(attributeName, memberName,
                        $"value type '{member.ValueType}' is not supported by item updates (scalar members only)");
                    continue;
                }

                if (rawValue == null)
                {
                    if (!member.IsOptional)
                    {
                        Fail(attributeName, memberName, "the member is mandatory and cannot be set to null");
                        continue;
                    }

                    mergedItem.Members[member.AttributeName] = null;
                    continue;
                }

                if (!TryConvert(member.ValueType, rawValue, out var converted))
                {
                    Fail(attributeName, memberName, $"the value is not a valid '{member.ValueType}' value");
                    continue;
                }

                mergedItem.Members[member.AttributeName] = converted;
            }
        }

        if (isInError)
        {
            return null;
        }

        return merged.Values
            .Select(m => new RtRecordArrayItemUpdate(m.Attribute, m.MatchMember, m.MatchValue, m.Members))
            .ToList();
    }

    private static CkTypeAttributeGraph? ResolveMember(ICkCacheService ckCache, string tenantId,
        CkRecordGraph recordGraph, string memberName)
    {
        if (recordGraph.AllAttributesByName.TryGetValue(memberName, out var member))
        {
            return member;
        }

        // The items of the array may be instances of derived records.
        foreach (var derivedId in recordGraph.GetAllDerivedRecords(false))
        {
            if (ckCache.TryGetCkRecord(tenantId, derivedId, out var derived) &&
                derived.AllAttributesByName.TryGetValue(memberName, out member))
            {
                return member;
            }
        }

        return null;
    }

    /// <summary>
    ///     Converts <paramref name="raw" /> to the CLR type of <paramref name="valueType" /> with the lenient conversion of
    ///     the regular write path; the result must end up with exactly the expected CLR type, otherwise the value is
    ///     refused. A String member takes text as it is and renders other scalars (numbers, booleans, dates as
    ///     ISO-8601) as text - a pipeline cannot tell a date text from a date, its data context turns ISO-8601 text into
    ///     <see cref="DateTime" />.
    /// </summary>
    internal static bool TryConvert(AttributeValueTypesDto valueType, object? raw, out object? converted)
    {
        converted = null;
        var value = Unwrap(raw);
        if (value == null)
        {
            return false;
        }

        try
        {
            if (valueType == AttributeValueTypesDto.String)
            {
                var text = value switch
                {
                    string s => s,
                    DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
                    DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
                    bool or sbyte or byte or short or ushort or int or uint or long or ulong or float or double
                        or decimal => Convert.ToString(value, CultureInfo.InvariantCulture),
                    _ => null
                };
                if (text == null)
                {
                    return false;
                }

                converted = text.Trim();
                return true;
            }

            var result = AttributeValueConverter.ConvertAttributeValue(valueType, value);
            var ok = valueType switch
            {
                AttributeValueTypesDto.Integer or AttributeValueTypesDto.Enum => result is int,
                AttributeValueTypesDto.Integer64 => result is long,
                AttributeValueTypesDto.Double => result is double,
                AttributeValueTypesDto.Boolean => result is bool,
                AttributeValueTypesDto.DateTime => result is DateTime,
                AttributeValueTypesDto.DateTimeOffset => result is DateTimeOffset,
                AttributeValueTypesDto.TimeSpan => result is TimeSpan,
                _ => false
            };
            if (!ok)
            {
                return false;
            }

            converted = result;
            return true;
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException)
        {
            return false;
        }
    }

    private static object? Unwrap(object? raw)
    {
        switch (raw)
        {
            case JValue { Type: JTokenType.Null }:
                return null;
            case JValue jValue:
                return jValue.Value;
            case JToken:
                return null;
            case Enum enumValue:
                return Convert.ToInt32(enumValue, CultureInfo.InvariantCulture);
            case JsonElement element:
                return element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString(),
                    JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null
                };
            default:
                return raw;
        }
    }

    private sealed class MergedItem(string attribute, string matchMember, object matchValue)
    {
        public string Attribute { get; } = attribute;
        public string MatchMember { get; } = matchMember;
        public object MatchValue { get; } = matchValue;
        public Dictionary<string, object?> Members { get; } = new(StringComparer.Ordinal);
    }
}
