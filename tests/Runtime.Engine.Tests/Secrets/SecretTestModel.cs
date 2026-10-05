using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Secrets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Secrets;

/// <summary>
///     AB#5532: a small hand-built CK model with Secret attributes (top-level, inside records with a
///     record key, a single record, nested records) and a faked CK cache serving it. The shared test CK
///     model is deliberately not changed - other repositories' tests depend on its shape.
/// </summary>
internal sealed class SecretTestModel
{
    public const string TenantId = "secret-tests";
    private const string ModelId = "Test-1.0.0";

    // Obviously fake test keys (0x42.. / 0x64.. / 0x00..0x1F) - the same vectors as SecretAttributeProtectorTests.
    public const string K1 = "QkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkI=";
    public const string V1Key = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
    public const string V1Vector = "enc:v1:oKGio6SlpqeoqaqrOxh7H702oGiyoSGkNg1vJ7bb2F42vG3NFkjx4iY=";
    public const string V1VectorPlaintext = "Pässwort-v1!";
    public static readonly string K2 = Convert.ToBase64String(Enumerable.Range(100, 32).Select(i => (byte)i).ToArray());

    public SecretTestModel()
    {
        Credential = BuildRecord("Credential", "Key",
            Attr("Key", AttributeValueTypesDto.String, isOptional: false),
            Attr("Value", AttributeValueTypesDto.Secret),
            Attr("Note", AttributeValueTypesDto.String));
        RequiredCredential = BuildRecord("RequiredCredential", "Key",
            Attr("Key", AttributeValueTypesDto.String, isOptional: false),
            Attr("Secret", AttributeValueTypesDto.Secret, isOptional: false));
        Wrapper = BuildRecord("Wrapper", "Name",
            Attr("Name", AttributeValueTypesDto.String, isOptional: false),
            Attr("Inner", AttributeValueTypesDto.Record, recordId: Credential.CkRecordId),
            Attr("Items", AttributeValueTypesDto.RecordArray, recordId: Credential.CkRecordId));
        Settings = BuildRecord("Settings", null, Attr("Timeout", AttributeValueTypesDto.Int));

        Config = BuildType("Config",
            Attr("Name", AttributeValueTypesDto.String),
            Attr("Password", AttributeValueTypesDto.Secret),
            Attr("ApiKey", AttributeValueTypesDto.Secret, isOptional: false),
            Attr("Credentials", AttributeValueTypesDto.RecordArray, recordId: Credential.CkRecordId),
            Attr("RequiredCredentials", AttributeValueTypesDto.RecordArray, recordId: RequiredCredential.CkRecordId),
            Attr("Primary", AttributeValueTypesDto.Record, recordId: Credential.CkRecordId),
            Attr("Wrappers", AttributeValueTypesDto.RecordArray, recordId: Wrapper.CkRecordId),
            Attr("Settings", AttributeValueTypesDto.Record, recordId: Settings.CkRecordId));
        OptionalOnly = BuildType("OptionalOnly",
            Attr("Name", AttributeValueTypesDto.String),
            Attr("Password", AttributeValueTypesDto.Secret));
        Plain = BuildType("Plain",
            Attr("Name", AttributeValueTypesDto.String),
            Attr("Settings", AttributeValueTypesDto.Record, recordId: Settings.CkRecordId));

        Cache = A.Fake<ICkCacheService>();
        A.CallTo(() => Cache.IsTenantLoaded(TenantId)).Returns(true);
        A.CallTo(() => Cache.GetCkTypes(TenantId)).Returns([Config, OptionalOnly, Plain]);
        foreach (var type in new[] { Config, OptionalOnly, Plain })
        {
            RegisterType(type);
        }

        foreach (var record in new[] { Credential, RequiredCredential, Wrapper, Settings })
        {
            RegisterRecord(record);
        }
    }

    public ICkCacheService Cache { get; }

    public CkTypeGraph Config { get; }

    public CkTypeGraph OptionalOnly { get; }

    public CkTypeGraph Plain { get; }

    public CkRecordGraph Credential { get; }

    public CkRecordGraph RequiredCredential { get; }

    public CkRecordGraph Wrapper { get; }

    public CkRecordGraph Settings { get; }

    public static SecretAttributeProtector CreateProtector(bool configured = true, string activeKeyId = "k1")
    {
        var options = configured
            ? new SecretEncryptionOptions
            {
                Keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["k1"] = K1, ["k2"] = K2 },
                ActiveKeyId = activeKeyId,
                LegacyV1Key = V1Key
            }
            : new SecretEncryptionOptions();
        return new SecretAttributeProtector(Options.Create(options), NullLogger<SecretAttributeProtector>.Instance);
    }

    public RtRecord CredentialRecord(string key, object? value, bool includeValue = true)
    {
        var record = new RtRecord { CkRecordId = Credential.CkRecordId.ToRtCkId() };
        record.SetAttributeRawValue("Key", key);
        if (includeValue)
        {
            record.SetAttributeRawValue("Value", value);
        }

        return record;
    }

    public RtRecord WrapperRecord(string name, RtRecord? inner, params RtRecord[] items)
    {
        var record = new RtRecord { CkRecordId = Wrapper.CkRecordId.ToRtCkId() };
        record.SetAttributeRawValue("Name", name);
        if (inner != null)
        {
            record.SetAttributeRawValue("Inner", inner);
        }

        record.SetAttributeRawValue("Items", items.ToList());
        return record;
    }

    public RtEntity NewConfig(OctoObjectId? rtId = null)
    {
        return new RtEntity(Config.CkTypeId.ToRtCkId(), rtId ?? OctoObjectId.GenerateNewId());
    }

    public RtEntity NewOptionalOnly(OctoObjectId? rtId = null)
    {
        return new RtEntity(OptionalOnly.CkTypeId.ToRtCkId(), rtId ?? OctoObjectId.GenerateNewId());
    }

    /// <summary>
    ///     Walks every attribute value (records included) and returns all strings found - used to assert
    ///     that no plaintext reaches a repository write.
    /// </summary>
    public static IEnumerable<string> AllStrings(RtTypeWithAttributes owner)
    {
        foreach (var value in owner.Attributes.Values)
        {
            foreach (var text in AllStrings(value))
            {
                yield return text;
            }
        }
    }

    private static IEnumerable<string> AllStrings(object? value)
    {
        switch (value)
        {
            case null:
                yield break;
            case string text:
                yield return text;
                break;
            case RtTypeWithAttributes nested:
                foreach (var text in AllStrings(nested))
                {
                    yield return text;
                }

                break;
            case System.Collections.IEnumerable items:
                foreach (var item in items)
                {
                    foreach (var text in AllStrings(item))
                    {
                        yield return text;
                    }
                }

                break;
        }
    }

    /// <summary>
    ///     Walks the Secret slots of an entity of <see cref="Config" /> / <see cref="OptionalOnly" /> and
    ///     returns their values.
    /// </summary>
    public IEnumerable<object?> SecretSlotValues(RtEntity entity)
    {
        foreach (var name in new[] { "Password", "ApiKey" })
        {
            if (entity.Attributes.TryGetValue(name, out var value))
            {
                yield return value;
            }
        }

        foreach (var record in Records(entity))
        {
            foreach (var name in new[] { "Value", "Secret" })
            {
                if (record.Attributes.TryGetValue(name, out var value))
                {
                    yield return value;
                }
            }
        }
    }

    private static IEnumerable<RtRecord> Records(RtTypeWithAttributes owner)
    {
        foreach (var value in owner.Attributes.Values)
        {
            switch (value)
            {
                case RtRecord record:
                    yield return record;
                    foreach (var nested in Records(record))
                    {
                        yield return nested;
                    }

                    break;
                case System.Collections.IEnumerable items and not string:
                    foreach (var record in items.OfType<RtRecord>())
                    {
                        yield return record;
                        foreach (var nested in Records(record))
                        {
                            yield return nested;
                        }
                    }

                    break;
            }
        }
    }

    private void RegisterType(CkTypeGraph type)
    {
        CkTypeGraph? ignored;
        var rtId = type.CkTypeId.ToRtCkId();
        A.CallTo(() => Cache.TryGetRtCkType(TenantId, A<RtCkId<CkTypeId>>.That.Matches(id => id.FullName == rtId.FullName),
                out ignored))
            .Returns(true).AssignsOutAndRefParameters(type);
        A.CallTo(() => Cache.GetRtCkType(TenantId, A<RtCkId<CkTypeId>>.That.Matches(id => id.FullName == rtId.FullName)))
            .Returns(type);
    }

    private void RegisterRecord(CkRecordGraph record)
    {
        CkRecordGraph? ignored;
        var rtId = record.CkRecordId.ToRtCkId();
        A.CallTo(() => Cache.TryGetRtCkRecord(TenantId,
                A<RtCkId<CkRecordId>>.That.Matches(id => id != null && id.FullName == rtId.FullName), out ignored))
            .Returns(true).AssignsOutAndRefParameters(record);
        A.CallTo(() => Cache.TryGetCkRecord(TenantId,
                A<CkId<CkRecordId>>.That.Matches(id => id != null && id.FullName == record.CkRecordId.FullName),
                out ignored))
            .Returns(true).AssignsOutAndRefParameters(record);
        A.CallTo(() => Cache.GetRtCkRecord(TenantId,
                A<RtCkId<CkRecordId>>.That.Matches(id => id != null && id.FullName == rtId.FullName)))
            .Returns(record);
    }

    private static CkTypeAttributeGraph Attr(string name, AttributeValueTypesDto valueType, bool isOptional = true,
        CkId<CkRecordId>? recordId = null)
    {
        var attributeId = new CkId<CkAttributeId>($"{ModelId}/{name}");
        var definition = new CkAttributeDto
        {
            AttributeId = name,
            ValueType = valueType,
            ValueCkRecordId = recordId,
            Ownership = valueType == AttributeValueTypesDto.Secret ? AttributeOwnershipDto.Secret : null
        };
        return new CkTypeAttributeGraph(attributeId,
            new CkTypeAttributeDto { CkAttributeId = attributeId, AttributeName = name, IsOptional = isOptional },
            new CkAttributeGraph(attributeId, definition));
    }

    private static CkRecordGraph BuildRecord(string name, string? recordKey, params CkTypeAttributeGraph[] attributes)
    {
        return new CkRecordGraph(new CkId<CkRecordId>($"{ModelId}/{name}"), false, false, [], null, [], [],
            attributes.ToDictionary(a => a.CkAttributeId, a => a), name)
        {
            RecordKey = recordKey
        };
    }

    private static CkTypeGraph BuildType(string name, params CkTypeAttributeGraph[] attributes)
    {
        return new CkTypeGraph(new CkId<CkTypeId>($"{ModelId}/{name}"), false, false, true, [], null, null, [], [],
            attributes.ToDictionary(a => a.CkAttributeId, a => a), [], new CkGraphDirectedAssociations([]), name, false);
    }
}
