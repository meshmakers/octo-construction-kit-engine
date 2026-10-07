using System.Text;
using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Meshmakers.Octo.Runtime.Engine.Blueprints;
using Meshmakers.Octo.Runtime.Engine.Exchange;
using Meshmakers.Octo.Runtime.Engine.Serialization;
using Meshmakers.Octo.Runtime.Engine.TransportContainer;
using Newtonsoft.Json;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Secrets;

/// <summary>
///     AB#5532: blueprint re-apply (import preservation, comparer), export and the engine Rt serialisers
///     never diff, overwrite, export or serialise a secret.
/// </summary>
public class SecretImportExportSerializationTests
{
    private const string Plain = "Plain-Text-Secret!";
    private readonly SecretTestModel _model = new();
    private readonly Engine.Secrets.SecretAttributeProtector _protector = SecretTestModel.CreateProtector();

    private RtCkId<CkAttributeId> AttrId(CkTypeWithAttributesGraph graph, string name) =>
        graph.AllAttributesByName[name].CkAttributeId.ToRtCkId();

    private RtRecordTcDto CredentialDto(string key, object? value)
    {
        var dto = new RtRecordTcDto { CkRecordId = _model.Credential.CkRecordId.ToRtCkId() };
        dto.Attributes.Add(new RtAttributeTcDto { Id = AttrId(_model.Credential, "Key"), Value = key });
        dto.Attributes.Add(new RtAttributeTcDto { Id = AttrId(_model.Credential, "Value"), Value = value });
        return dto;
    }

    private CkRecordGraph? ResolveRecord(RtCkId<CkRecordId> id) =>
        id.FullName == _model.Credential.CkRecordId.ToRtCkId().FullName ? _model.Credential :
        id.FullName == _model.Wrapper.CkRecordId.ToRtCkId().FullName ? _model.Wrapper : null;

    #region Import preservation (blueprint re-apply)

    [Fact]
    public void PreserveAttributesForEntity_StoredLegacyString_IsCarriedAsLegacy()
    {
        var existing = _model.NewConfig();
        existing.SetAttributeRawValue("ApiKey", SecretTestModel.V1Vector);
        var seed = new RtEntityTcDto { RtId = existing.RtId, CkTypeId = existing.CkTypeId! };
        seed.Attributes.Add(new RtAttributeTcDto { Id = AttrId(_model.Config, "ApiKey"), Value = "" });

        var preserved = ImportRtModelCommand.PreserveAttributesForEntity(seed, existing,
            [_model.Config.AllAttributesByName["ApiKey"]], v => v);

        Assert.Equal(1, preserved);
        var value = Assert.IsType<RtSecretValue>(seed.Attributes.Single().Value);
        Assert.True(value.IsLegacyPlaintext); // not a plain string = not new input, no double encryption
    }

    [Fact]
    public void PreserveSecretRecordMembers_KeepsStoredSecretOfTheSameKey()
    {
        var stored = _protector.Protect(Plain);
        var existing = _model.NewConfig();
        existing.SetAttributeRawValue("Credentials", new List<RtRecord>
        {
            _model.CredentialRecord("a", stored), _model.CredentialRecord("b", "legacy-b")
        });
        var seed = new RtEntityTcDto { RtId = existing.RtId, CkTypeId = existing.CkTypeId! };
        seed.Attributes.Add(new RtAttributeTcDto
        {
            Id = AttrId(_model.Config, "Credentials"),
            Value = new List<object> { CredentialDto("b", ""), CredentialDto("a", null), CredentialDto("c", "") }
        });

        var preserved = ImportRtModelCommand.PreserveSecretRecordMembers(seed, existing,
            [_model.Config.AllAttributesByName["Credentials"]], ResolveRecord);

        Assert.Equal(2, preserved);
        var elements = ((List<object>)seed.Attributes.Single().Value!).Cast<RtRecordTcDto>().ToList();
        Assert.Equal(RtSecretValue.LegacyPlaintext("legacy-b"), elements[0].Attributes[1].Value);
        Assert.Same(stored, elements[1].Attributes[1].Value);
        Assert.Equal("", elements[2].Attributes[1].Value); // no stored element with key c
    }

    [Fact]
    public void PreserveSecretRecordMembers_PlaceholderLookingSeedValue_IsAValue_AndNotReplaced()
    {
        // Decisions 2026-10-06 item 1 (the seed lint rejects such a seed; the import treats it as a value).
        var existing = _model.NewConfig();
        existing.SetAttributeRawValue("Credentials", new List<RtRecord> { _model.CredentialRecord("a", _protector.Protect(Plain)) });
        var seed = new RtEntityTcDto { RtId = existing.RtId, CkTypeId = existing.CkTypeId! };
        seed.Attributes.Add(new RtAttributeTcDto
        {
            Id = AttrId(_model.Config, "Credentials"), Value = new List<object> { CredentialDto("a", "TODO_SET_A") }
        });

        var preserved = ImportRtModelCommand.PreserveSecretRecordMembers(seed, existing,
            [_model.Config.AllAttributesByName["Credentials"]], ResolveRecord);

        Assert.Equal(0, preserved);
        var element = ((List<object>)seed.Attributes.Single().Value!).Cast<RtRecordTcDto>().Single();
        Assert.Equal("TODO_SET_A", element.Attributes[1].Value);
    }

    [Fact]
    public void FindMissingMandatoryAttributes_EmptySecretCountsAsMissing()
    {
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("ApiKey", RtSecretValue.Pending(""));

        var missing = ImportRtModelCommand.FindMissingMandatoryAttributes(_model.Config.AllAttributes.Values, entity);

        Assert.Contains(missing, a => a.AttributeName == "ApiKey");
    }

    [Fact]
    public void FindMissingMandatoryAttributes_PlaceholderLookingSecret_AndUnreadableSecret_CountAsPresent()
    {
        var placeholder = _model.NewConfig();
        placeholder.SetAttributeRawValue("ApiKey", RtSecretValue.Pending("TODO_SET_API_KEY"));
        var unreadable = _model.NewConfig();
        unreadable.SetAttributeRawValue("ApiKey",
            RtSecretValue.Protected("enc:v2:k9:AAECAwQFBgcICQoLce30XInwk1La5ADQECWjFW2r_6nUXsA"));

        Assert.DoesNotContain(ImportRtModelCommand.FindMissingMandatoryAttributes(_model.Config.AllAttributes.Values,
            placeholder), a => a.AttributeName == "ApiKey");
        Assert.DoesNotContain(ImportRtModelCommand.FindMissingMandatoryAttributes(_model.Config.AllAttributes.Values,
            unreadable), a => a.AttributeName == "ApiKey");
    }

    #endregion

    #region Blueprint comparer

    [Fact]
    public void Comparer_NeverDiffsSecrets_EvenWithoutSecretOwnership()
    {
        // A stale cache could carry a Secret attribute with seed ownership - the value type alone decides.
        var attributeId = new CkId<CkAttributeId>("Test-1.0.0/Token");
        var attribute = new CkTypeAttributeGraph(attributeId, "Token", null, AttributeValueTypesDto.Secret, null, null,
            null, null, null, true, null);
        var type = new CkTypeGraph(new CkId<CkTypeId>("Test-1.0.0/Stale"), false, false, true, [], null, null, [], [],
            new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph> { [attributeId] = attribute }, [],
            new CkGraphDirectedAssociations([]), "stale", false);
        var tenant = new RtEntity(type.CkTypeId.ToRtCkId(), OctoObjectId.GenerateNewId());
        tenant.SetAttributeRawValue("Token", _protector.Protect(Plain));
        var seed = new RtEntityTcDto { RtId = tenant.RtId, CkTypeId = tenant.CkTypeId! };
        seed.Attributes.Add(new RtAttributeTcDto { Id = attributeId.ToRtCkId(), Value = "<TOKEN>" });

        var changes = BlueprintEntityComparer.Compare(seed, tenant, type, v => v, _ => null);

        Assert.Empty(changes);
    }

    [Fact]
    public void Comparer_RecordWithSecretMember_NoPhantomChange_AndNoCiphertextInReportedChanges()
    {
        var envelope = _protector.Protect(Plain);
        var tenant = _model.NewConfig();
        tenant.SetAttributeRawValue("Credentials", new List<RtRecord> { _model.CredentialRecord("a", envelope) });
        var seed = new RtEntityTcDto { RtId = tenant.RtId, CkTypeId = tenant.CkTypeId! };
        seed.Attributes.Add(new RtAttributeTcDto
        {
            Id = AttrId(_model.Config, "Credentials"), Value = new List<object> { CredentialDto("a", "<SET>") }
        });

        object? ToTransport(object? value) => value switch
        {
            IEnumerable<RtRecord> records => records.Select(r =>
            {
                var dto = new RtRecordTcDto { CkRecordId = r.CkRecordId };
                foreach (var (name, v) in r.Attributes)
                {
                    dto.Attributes.Add(new RtAttributeTcDto { Id = AttrId(_model.Credential, name), Value = v });
                }

                return (object)dto;
            }).ToList(),
            _ => value
        };

        var unchanged = BlueprintEntityComparer.Compare(seed, tenant, _model.Config, ToTransport, _ => null, ResolveRecord);
        Assert.DoesNotContain(unchanged, c => c.AttributeName == "Credentials");

        // A real change in another member is reported - with the secret member masked on both sides.
        ((List<object>)seed.Attributes.Single(a => a.Id.Equals(AttrId(_model.Config, "Credentials"))).Value!)
            .Add(CredentialDto("b", "TODO_SET_B"));
        var changed = BlueprintEntityComparer.Compare(seed, tenant, _model.Config, ToTransport, _ => null, ResolveRecord);
        var change = Assert.Single(changed, c => c.AttributeName == "Credentials");
        var values = MemberValues(change.OldValue).Concat(MemberValues(change.NewValue)).ToList();
        Assert.DoesNotContain(values, v => v is RtSecretValue);
        Assert.DoesNotContain(values, v => v is string text && (text.Contains("enc:v") || text == "TODO_SET_B" || text == "<SET>"));
        Assert.Contains(values, v => Equals(v, RtSecretValue.Mask));
    }

    [Fact]
    public void Comparer_RecordConversionFails_FallbackChangeStillMasksSecretMembers()
    {
        // The fallback reports the raw repository values when a record conversion throws (AB#5532 review):
        // a legacy clear-text string in a Secret member of a raw RtRecord must not leave the comparer.
        var tenant = _model.NewConfig();
        tenant.SetAttributeRawValue("Credentials", new List<RtRecord> { _model.CredentialRecord("a", Plain) });
        var seed = new RtEntityTcDto { RtId = tenant.RtId, CkTypeId = tenant.CkTypeId! };
        seed.Attributes.Add(new RtAttributeTcDto
        {
            Id = AttrId(_model.Config, "Credentials"), Value = new List<object> { CredentialDto("a", "<SET>") }
        });

        object? Throwing(object? value) =>
            value is IEnumerable<RtRecord> ? throw new InvalidOperationException("stale record") : value;

        var changes = BlueprintEntityComparer.Compare(seed, tenant, _model.Config, Throwing, _ => null, ResolveRecord);

        var change = Assert.Single(changes, c => c.AttributeName == "Credentials");
        var stored = Assert.IsAssignableFrom<System.Collections.IEnumerable>(change.OldValue).Cast<object>().ToList();
        var record = Assert.IsType<RtRecord>(Assert.Single(stored));
        Assert.Equal(RtSecretValue.Mask, record.Attributes["Value"]);
        Assert.Equal("a", record.Attributes["Key"]);
        Assert.DoesNotContain(MemberValues(change.NewValue), v => v is string text && text == "<SET>");
    }

    private static IEnumerable<object?> MemberValues(object? value)
    {
        switch (value)
        {
            case RtRecordTcDto record:
                foreach (var attribute in record.Attributes)
                {
                    yield return attribute.Value;
                    foreach (var nested in MemberValues(attribute.Value))
                    {
                        yield return nested;
                    }
                }

                break;
            case System.Collections.IEnumerable items and not string:
                foreach (var item in items)
                {
                    foreach (var nested in MemberValues(item))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }

    #endregion

    #region Export and serialisation

    [Fact]
    public void Export_OmitsSecretValues()
    {
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("Name", "visible");
        entity.SetAttributeRawValue("ApiKey", _protector.Protect(Plain));
        entity.SetAttributeRawValue("Password", RtSecretValue.LegacyPlaintext(Plain));

        var dto = new RtEntityToTcDtoConverter(_model.Cache).Convert(SecretTestModel.TenantId, entity);

        Assert.Contains(dto.Attributes, a => Equals(a.Value, "visible"));
        Assert.DoesNotContain(dto.Attributes, a => a.Id.Equals(AttrId(_model.Config, "ApiKey")));
        Assert.DoesNotContain(dto.Attributes, a => a.Id.Equals(AttrId(_model.Config, "Password")));
    }

    private RtEntity EntityWithSecrets()
    {
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("ApiKey", _protector.Protect(Plain));
        entity.SetAttributeRawValue("Password", RtSecretValue.Pending("pending-" + Plain));
        entity.SetAttributeRawValue("Credentials", new List<RtRecord>
        {
            _model.CredentialRecord("a", RtSecretValue.LegacyPlaintext("legacy-" + Plain)),
            _model.CredentialRecord("b", RtSecretValue.Pending(""))
        });
        return entity;
    }

    [Fact]
    public void SystemTextJson_WritesOnlyTheMarker()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(EntityWithSecrets(), RtSystemTextJsonSerializer.Default);

        Assert.DoesNotContain(Plain, json);
        Assert.DoesNotContain("enc:v", json);
        Assert.Contains("\"isSet\":true", json);
        Assert.Contains("\"isSet\":false", json);
        // Also with plain default options (type-level converter).
        var plainJson = System.Text.Json.JsonSerializer.Serialize(_protector.Protect(Plain));
        Assert.Equal("{\"isSet\":true}", plainJson);
    }

    [Fact]
    public void Newtonsoft_WritesOnlyTheMarker()
    {
        var writer = new StringWriter();
        RtNewtonsoftSerializer.DefaultSerializer.Serialize(writer, EntityWithSecrets());
        var json = writer.ToString();

        Assert.DoesNotContain(Plain, json);
        Assert.DoesNotContain("enc:v", json);
        Assert.Contains("isSet", json);
        Assert.Equal("{\"isSet\":true}", JsonConvert.SerializeObject(_protector.Protect(Plain)));
    }

    [Fact]
    public void Yaml_WritesOnlyTheMarker()
    {
        var serializer = new SerializerBuilder().WithTypeConverter(new RtSecretValueYamlConverter()).Build();

        var yaml = serializer.Serialize(new Dictionary<string, object?>
        {
            ["a"] = _protector.Protect(Plain), ["b"] = RtSecretValue.Pending(Plain)
        });

        Assert.DoesNotContain(Plain, yaml);
        Assert.DoesNotContain("enc:v", yaml);
        Assert.Contains("isSet: true", yaml);
    }

    [Theory]
    [InlineData("\"new-value\"", "new-value")]
    [InlineData("{\"isSet\":true}", "")]
    [InlineData("{}", "")]
    public void Json_Read_StringIsInput_MarkerIsUnchanged(string json, string expectedPending)
    {
        var stj = System.Text.Json.JsonSerializer.Deserialize<RtSecretValue>(json)!;
        var newtonsoft = JsonConvert.DeserializeObject<RtSecretValue>(json)!;

        Assert.True(stj.IsPending);
        Assert.Equal(RtSecretValue.Pending(expectedPending), stj);
        Assert.Equal(RtSecretValue.Pending(expectedPending), newtonsoft);
        Assert.Null(System.Text.Json.JsonSerializer.Deserialize<RtSecretValue>("null"));
    }

    [Theory]
    [InlineData("{\"isSet\":false}")]
    [InlineData("{\"IsSet\":true}")]
    // AB#5532 round 2: the echoed read state object (OctoSecretState) is the marker as well.
    [InlineData("{\"isSet\":false,\"keyMissing\":true,\"setAt\":null}")]
    [InlineData("{\"isSet\":true,\"keyMissing\":false,\"setAt\":\"2026-10-06T12:34:56.789Z\"}")]
    [InlineData("{\"ISSET\":true,\"KeyMissing\":false,\"SETAT\":\"2026-10-06T12:34:56+02:00\"}")]
    [InlineData("{\"keyMissing\":true}")]
    [InlineData("{\"setAt\":\"2026-10-06\"}")]
    public void Json_Read_MarkerVariants_AreUnchanged(string json)
    {
        Assert.Equal(RtSecretValue.Pending(""), System.Text.Json.JsonSerializer.Deserialize<RtSecretValue>(json));
        Assert.Equal(RtSecretValue.Pending(""), JsonConvert.DeserializeObject<RtSecretValue>(json));
    }

    [Theory]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("[\"hunter2\"]")]
    [InlineData("{\"envelope\":\"enc:v2:k1:hunter2\"}")]
    [InlineData("{\"isSet\":true,\"value\":\"hunter2\"}")]
    [InlineData("{\"isSet\":\"hunter2\"}")]
    [InlineData("{\"isSet\":1}")]
    [InlineData("{\"isSet\":null}")]
    [InlineData("{\"keyMissing\":\"hunter2\"}")]
    [InlineData("{\"keyMissing\":null}")]
    [InlineData("{\"setAt\":\"hunter2\"}")]
    [InlineData("{\"setAt\":42}")]
    [InlineData("{\"setAt\":true}")]
    [InlineData("{\"setAt\":{\"value\":\"hunter2\"}}")]
    [InlineData("{\"isSet\":true,\"keyMissing\":false,\"setAt\":null,\"value\":\"hunter2\"}")]
    public void Json_Read_AnythingElse_Throws_WithoutEchoingTheValue(string json)
    {
        var stj = Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<RtSecretValue>(json));
        AssertNoValue(stj.Message);

        var newtonsoft = Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() =>
            JsonConvert.DeserializeObject<RtSecretValue>(json));
        AssertNoValue(newtonsoft.Message);

        // Also inside a containing object (the converter must leave the reader consistent).
        var wrapped = "{\"a\":" + json + "}";
        Assert.Throws<System.Text.Json.JsonException>(() =>
            System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, RtSecretValue?>>(wrapped));
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() =>
            JsonConvert.DeserializeObject<Dictionary<string, RtSecretValue?>>(wrapped));
    }

    [Fact]
    public void Json_Read_MarkerInsideObject_LeavesTheReaderOnTheNextProperty()
    {
        const string json = "{\"a\":{\"isSet\":true},\"b\":\"new\",\"c\":null}";

        var stj = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, RtSecretValue?>>(json)!;
        var newtonsoft = JsonConvert.DeserializeObject<Dictionary<string, RtSecretValue?>>(json)!;

        foreach (var result in new[] { stj, newtonsoft })
        {
            Assert.Equal(RtSecretValue.Pending(""), result["a"]);
            Assert.Equal(RtSecretValue.Pending("new"), result["b"]);
            Assert.Null(result["c"]);
        }
    }

    [Theory]
    [InlineData("new-value", "new-value")]
    [InlineData("{isSet: true}", "")]
    [InlineData("{isSet: false}", "")]
    [InlineData("{}", "")]
    [InlineData("{isSet: false, keyMissing: true, setAt: null}", "")]
    [InlineData("{isSet: true, keyMissing: false, setAt: ~}", "")]
    [InlineData("{isSet: true, keyMissing: false, setAt: 2026-10-06T12:34:56.789Z}", "")]
    [InlineData("{IsSet: true, SetAt: '2026-10-06T12:34:56Z'}", "")]
    public void Yaml_Read_ScalarIsInput_MarkerIsUnchanged(string yaml, string expectedPending)
    {
        var deserializer = new DeserializerBuilder().WithTypeConverter(new RtSecretValueYamlConverter()).Build();

        Assert.Equal(RtSecretValue.Pending(expectedPending), deserializer.Deserialize<RtSecretValue>(yaml));
    }

    [Theory]
    [InlineData("[hunter2]")]
    [InlineData("{envelope: hunter2}")]
    [InlineData("{isSet: true, value: hunter2}")]
    [InlineData("{isSet: hunter2}")]
    [InlineData("{isSet: 'true'}")]
    [InlineData("{keyMissing: hunter2}")]
    [InlineData("{setAt: hunter2}")]
    [InlineData("{setAt: [hunter2]}")]
    [InlineData("{isSet: true, setAt: null, value: hunter2}")]
    public void Yaml_Read_AnythingElse_Throws_WithoutEchoingTheValue(string yaml)
    {
        var deserializer = new DeserializerBuilder().WithTypeConverter(new RtSecretValueYamlConverter()).Build();

        var exception = Assert.ThrowsAny<YamlDotNet.Core.YamlException>(() => deserializer.Deserialize<RtSecretValue>(yaml));

        for (Exception? e = exception; e != null; e = e.InnerException)
        {
            AssertNoValue(e.Message);
        }
    }

    private static void AssertNoValue(string message)
    {
        Assert.DoesNotContain("hunter2", message, StringComparison.Ordinal);
        Assert.DoesNotContain("enc:", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AttributeValueConverter_MarkerSentBack_MeansUnchanged()
    {
        using var document = JsonDocument.Parse("{\"isSet\":true}");
        var fromElement = AttributeValueConverter.ConvertAttributeValue(AttributeValueTypesDto.Secret, document.RootElement);
        var fromDictionary = AttributeValueConverter.ConvertAttributeValue(AttributeValueTypesDto.Secret,
            new Dictionary<string, object?> { ["isSet"] = true });
        var fromJObject = AttributeValueConverter.ConvertAttributeValue(AttributeValueTypesDto.Secret,
            Newtonsoft.Json.Linq.JObject.Parse("{\"isSet\":true}"));

        Assert.Equal(RtSecretValue.Pending(""), fromElement);
        Assert.Equal(RtSecretValue.Pending(""), fromDictionary);
        Assert.Equal(RtSecretValue.Pending(""), fromJObject);
    }

    [Fact]
    public void AttributeValueConverter_EchoedStateObject_MeansUnchanged()
    {
        // AB#5532 round 2: a client echoing { isSet, keyMissing, setAt } as read leaves the secret unchanged.
        const string state = "{\"isSet\":true,\"keyMissing\":false,\"setAt\":\"2026-10-06T12:34:56.789Z\"}";
        using var document = JsonDocument.Parse(state);
        object[] echoes =
        [
            document.RootElement,
            // JObject.Parse turns the ISO string into a Date token (default DateParseHandling).
            Newtonsoft.Json.Linq.JObject.Parse(state),
            new Dictionary<string, object?>
            {
                ["isSet"] = false, ["keyMissing"] = true, ["setAt"] = null
            },
            new Dictionary<string, object?>
            {
                ["isSet"] = true, ["keyMissing"] = false, ["setAt"] = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc)
            },
            new Dictionary<string, object?> { ["isSet"] = true, ["setAt"] = DateTimeOffset.UtcNow },
            new Dictionary<string, object?> { ["isSet"] = true, ["setAt"] = "2026-10-06T12:34:56Z" },
            // YAML shape: object keys, text scalars.
            new Dictionary<object, object?> { ["isSet"] = "true", ["keyMissing"] = "false", ["setAt"] = "2026-10-06T12:34:56Z" }
        ];

        foreach (var echo in echoes)
        {
            Assert.Equal(RtSecretValue.Pending(""),
                AttributeValueConverter.ConvertAttributeValue(AttributeValueTypesDto.Secret, echo));
        }
    }

    [Fact]
    public void AttributeValueConverter_StateObjectWithInvalidOptionalFields_IsRejectedWithoutTheValue()
    {
        using var document = JsonDocument.Parse("{\"isSet\":true,\"setAt\":\"hunter2\"}");
        object[] invalid =
        [
            document.RootElement,
            Newtonsoft.Json.Linq.JObject.Parse("{\"isSet\":true,\"keyMissing\":\"hunter2\"}"),
            new Dictionary<string, object?> { ["isSet"] = true, ["setAt"] = "hunter2" },
            new Dictionary<string, object?> { ["isSet"] = true, ["setAt"] = 42 },
            new Dictionary<string, object?> { ["keyMissing"] = "hunter2" },
            new Dictionary<object, object?> { ["setAt"] = "hunter2" }
        ];

        foreach (var value in invalid)
        {
            var exception = Assert.Throws<InvalidAttributeValueException>(() =>
                AttributeValueConverter.ConvertAttributeValue(AttributeValueTypesDto.Secret, value));
            AssertNoValue(exception.Message);
        }
    }

    [Fact]
    public void EntityUpdateInfo_ClearList_RoundTripsThroughBothSerializers()
    {
        var entity = _model.NewConfig();
        var info = EntityUpdateInfo<RtEntity>.CreateUpdate(new RtEntityId(entity.CkTypeId!, entity.RtId), entity,
            ["Password"]);

        var stjJson = System.Text.Json.JsonSerializer.Serialize(info, RtSystemTextJsonSerializer.Default);
        var stj = System.Text.Json.JsonSerializer.Deserialize<EntityUpdateInfo<RtEntity>>(stjJson,
            RtSystemTextJsonSerializer.Default)!;
        var writer = new StringWriter(new StringBuilder());
        RtNewtonsoftSerializer.DefaultSerializer.Serialize(writer, info);
        var newtonsoft = RtNewtonsoftSerializer.DefaultSerializer.Deserialize<EntityUpdateInfo<RtEntity>>(
            new JsonTextReader(new StringReader(writer.ToString())))!;

        Assert.Equal(["Password"], stj.ClearSecretAttributes);
        Assert.Equal(["Password"], newtonsoft.ClearSecretAttributes);

        var withoutClear = System.Text.Json.JsonSerializer.Serialize(
            EntityUpdateInfo<RtEntity>.CreateUpdate(new RtEntityId(entity.CkTypeId!, entity.RtId), entity),
            RtSystemTextJsonSerializer.Default);
        Assert.DoesNotContain("ClearSecretAttributes", withoutClear, StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
