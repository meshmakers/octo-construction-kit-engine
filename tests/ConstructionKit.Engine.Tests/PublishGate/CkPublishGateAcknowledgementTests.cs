using System.Text;
using System.Text.Json.Nodes;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer;
using static Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer.Rows.RowTestSupport;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.PublishGate;

/// <summary>
///     AB#6387: the acknowledgements of the compiled model are enforced at publish. Every test publishes a compiled
///     model that went through JSON (no <c>ckModel.yaml</c> exists), against an in-memory catalog (operating rule O1:
///     no shared catalog).
/// </summary>
public sealed class CkPublishGateAcknowledgementTests : IDisposable
{
    // The key the gate prints for the tightened attribute; derived, so the test does not depend on its spelling.
    private static readonly string SecretKey = CkChangeKey.Of(
        Assert.Single(Classify(Versioned("1.4.0"), Versioned("1.5.0", Tighten))).Change);

    private readonly GateHarness _h = new();

    public void Dispose() => _h.Dispose();

    private static string[] Codes(CkPublishGateResult r) => GateHarness.ErrorCodes(r);

    private static CkCompiledModelRoot Versioned(string version, Action<CkCompiledModelRoot>? change = null)
    {
        var model = Model();
        model.ModelId = new CkModelId(M, version);
        SemVerTestModels.GetAttribute(model, "SerialNumber").SecuritySensitive = true;
        change?.Invoke(model);
        return model;
    }

    /// <summary>
    ///     A model the catalog service can resolve (no dependency, no type that needs a base): a record whose
    ///     security-sensitive attribute is the part the tests tighten.
    /// </summary>
    private static CkCompiledModelRoot Publishable(string version, CkAttributeAccessDto? access = null) => new()
    {
        ModelId = new CkModelId(M, version),
        Description = "Publishable fixture",
        CkLanguage = 2,
        Attributes = [new CkAttributeDto { AttributeId = "Secret", ValueType = AttributeValueTypesDto.String, SecuritySensitive = true }],
        Records =
        [
            new CkRecordDto
            {
                RecordId = "Login",
                Attributes =
                [
                    new CkTypeAttributeDto
                    {
                        CkAttributeId = $"{M}-{version}/Secret", AttributeName = "Secret", IsOptional = true,
                        Access = access
                    }
                ]
            }
        ]
    };

    private static readonly string RecordKey = CkChangeKey.Of(
        Assert.Single(Classify(Publishable("1.4.0"), Publishable("1.5.0", CkAttributeAccessDto.Hidden))).Change);

    private static void Tighten(CkCompiledModelRoot model) =>
        Machine(model).Attributes!.Single(a => a.AttributeName == "SerialNumber").Access = CkAttributeAccessDto.Hidden;

    private static void AddUniqueIndex(CkCompiledModelRoot model) =>
        Machine(model).Indexes!.Add(new CkTypeIndexDto
        {
            IndexType = IndexTypeDto.Unique, Fields = [new CkIndexFieldsDto { AttributePaths = ["serialNumber"] }]
        });

    /// <summary>
    ///     What the publisher gets: the compiled model as JSON, with the <c>compatibility</c> section edited by hand
    ///     (<paramref name="entries" /> as written in the file, null leaves the file as compiled).
    /// </summary>
    private async Task<CkCompiledModelRoot> FromJsonAsync(CkCompiledModelRoot model,
        IEnumerable<(string? Change, string? Reason)>? entries)
    {
        var serializer = _h.Get<ICkJsonSerializer>();
        using var stream = new MemoryStream();
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true))
        {
            await serializer.SerializeAsync(writer, model);
        }

        var root = JsonNode.Parse(Encoding.UTF8.GetString(stream.ToArray()))!.AsObject();
        if (entries != null)
        {
            root["compatibility"] = new JsonObject
            {
                ["acknowledge"] = new JsonArray(entries.Select(e => (JsonNode)new JsonObject
                {
                    ["change"] = e.Change, ["reason"] = e.Reason
                }).ToArray())
            };
        }

        var operationResult = new OperationResult();
        var read = await serializer.DeserializeCompiledModelRootAsync(root.ToJsonString(), "forged.json",
            operationResult);
        Assert.False(operationResult.HasErrors);
        return read;
    }

    /// <summary>A hand-edited file the schema check of the reader would already stop: the gate must not rely on it.</summary>
    private static CkCompiledModelRoot Forged(CkCompiledModelRoot model,
        params (string? Change, string? Reason)[] entries)
    {
        model.Compatibility = new CkCompatibilityDto
        {
            Acknowledge = entries.Select(e => new CkAcknowledgeDto { Change = e.Change!, Reason = e.Reason! }).ToList()
        };
        return model;
    }

    private async Task SeedBaselineAsync() => await _h.SeedAsync(Versioned("1.4.0"));

    [Fact]
    public async Task A_security_tightening_without_an_acknowledgement_is_refused_with_CK203_naming_the_key_and_nothing_is_written()
    {
        await _h.SeedAsync(Publishable("1.4.0"));
        var model = await FromJsonAsync(Publishable("1.5.0", CkAttributeAccessDto.Hidden), null);

        var exception = await Assert.ThrowsAsync<CkPublishRefusedException>(() => _h.PublishAsync(model));

        Assert.Contains("OCTO-CK203", exception.AllText());
        Assert.Contains($"\"{RecordKey}\"", exception.AllText());
        Assert.Equal(0, _h.Stub.PublishCount);
        Assert.DoesNotContain(_h.Stub.ModelIds, id => id.Version == new CkVersion("1.5.0"));
    }

    [Fact]
    public async Task A_matching_entry_publishes_and_the_log_lists_the_acknowledged_change_with_its_reason()
    {
        await _h.SeedAsync(Publishable("1.4.0"));
        var model = await FromJsonAsync(Publishable("1.5.0", CkAttributeAccessDto.Hidden),
            [(RecordKey, "Close accepted risk R13")]);

        var result = await _h.EvaluateAsync(model);
        await _h.PublishAsync(model);

        Assert.Equal(CkPublishGateDecision.Proceed, result.Decision);
        Assert.Contains(result.Messages, m => m.Text.StartsWith("Acknowledged changes of model "));
        Assert.Contains(result.Messages,
            m => m.Text.Contains("'Login-1/Secret': access changed") && m.Text.Contains("acknowledged: Close accepted risk R13") &&
                 m.Text.Contains("MINOR"));
        Assert.Single(result.Verdict!.Acknowledgement.Acknowledged);
        Assert.Equal(1, _h.Stub.PublishCount);
    }

    [Fact]
    public async Task A_stale_entry_is_refused_with_CK204()
    {
        await SeedBaselineAsync();
        var model = await FromJsonAsync(Versioned("1.5.0"), [(SecretKey, "left over from the last release")]);

        var result = await _h.EvaluateAsync(model);

        Assert.Equal(CkPublishGateDecision.Refused, result.Decision);
        Assert.Equal(["OCTO-CK204"], Codes(result));
    }

    [Fact]
    public async Task An_entry_on_a_first_publication_is_stale()
    {
        var model = await FromJsonAsync(Versioned("1.0.0"), [(SecretKey, "nothing to acknowledge yet")]);

        var result = await _h.EvaluateAsync(model);

        Assert.Equal(CkPublishGateDecision.Refused, result.Decision);
        Assert.Equal(["OCTO-CK204"], Codes(result));
    }

    [Fact]
    public async Task An_entry_for_a_removed_public_attribute_does_not_waive_CK200()
    {
        await SeedBaselineAsync();
        var current = Versioned("1.5.0", m => Machine(m).Attributes!.RemoveAll(a => a.AttributeName == "State"));
        var removedKey = CkChangeKey.Of(Assert.Single(Classify(Versioned("1.4.0"), current)).Change);
        var model = await FromJsonAsync(current, [(removedKey, "we do not need it any more")]);

        var result = await _h.EvaluateAsync(model);

        Assert.Equal(CkPublishGateDecision.Refused, result.Decision);
        // The ordinary change is still uncovered; the entry for it is stale and says why.
        Assert.Equal(["OCTO-CK200", "OCTO-CK204"], Codes(result));
        Assert.Contains("needs no acknowledgement", result.Messages.Single(m => m.Code == "OCTO-CK204").Text);
    }

    [Fact]
    public async Task An_entry_for_a_unique_index_on_a_stable_base_does_not_waive_the_major_bump()
    {
        await SeedBaselineAsync();
        var current = Versioned("1.5.0", AddUniqueIndex);
        var key = CkChangeKey.Of(Assert.Single(Classify(Versioned("1.4.0"), current)).Change);

        var tooLow = await _h.EvaluateAsync(await FromJsonAsync(current, [(key, "cleaned up")]));
        var major = Versioned("2.0.0", AddUniqueIndex);
        var enough = await _h.EvaluateAsync(await FromJsonAsync(major, [(key, "cleaned up")]));

        Assert.Equal(["OCTO-CK200"], Codes(tooLow));
        Assert.Contains("MAJOR", tooLow.Messages.Single(m => m.Code == "OCTO-CK200").Text);
        Assert.Equal(CkPublishGateDecision.Proceed, enough.Decision);
        Assert.Single(enough.Verdict!.Acknowledgement.Acknowledged);
    }

    [Fact]
    public async Task A_missing_acknowledgement_is_still_refused_next_to_a_missing_major_bump()
    {
        await SeedBaselineAsync();

        var result = await _h.EvaluateAsync(await FromJsonAsync(Versioned("1.5.0", AddUniqueIndex), null));

        Assert.Equal(["OCTO-CK200", "OCTO-CK203"], Codes(result));
    }

    public static TheoryData<string, string?, string?> ForgedEntries => new()
    {
        { "empty reason", RecordKey, "" },
        { "blank reason", RecordKey, "   " },
        { "missing reason", RecordKey, null },
        { "wildcard", RecordKey.Replace("Secret", "*"), "accepted" },
        { "question mark", RecordKey.Replace("Secret", "Secre?"), "accepted" },
        { "empty key", "", "accepted" },
        { "control character", RecordKey, "accepted\nbut hidden" },
        { "pipeline command", RecordKey, "##vso[task.complete result=Succeeded]" }
    };

    [Theory]
    [MemberData(nameof(ForgedEntries))]
    public async Task A_forged_entry_in_the_compiled_json_is_refused_with_CK206_and_nothing_is_written(string forgery,
        string? change, string? reason)
    {
        _ = forgery;
        await _h.SeedAsync(Publishable("1.4.0"));
        var model = Forged(Publishable("1.5.0", CkAttributeAccessDto.Hidden), (change, reason));

        var exception = await Assert.ThrowsAsync<CkPublishRefusedException>(() => _h.PublishAsync(model));

        Assert.Contains("OCTO-CK206", exception.AllText());
        Assert.Equal(0, _h.Stub.PublishCount);
    }

    [Theory]
    [InlineData("", "accepted")]
    [InlineData("RecordAttribute:*", "accepted")]
    [InlineData("RecordAttribute:Login-1/Secret#Modified:access", "")]
    public async Task The_reader_already_stops_a_hand_edited_file_with_an_empty_key_wildcard_or_empty_reason(
        string change, string reason)
    {
        await Assert.ThrowsAsync<ModelParseException>(() =>
            FromJsonAsync(Publishable("1.5.0", CkAttributeAccessDto.Hidden), [(change, reason)]));
    }

    [Fact]
    public async Task A_duplicate_key_is_refused_with_CK206()
    {
        await _h.SeedAsync(Publishable("1.4.0"));
        var model = Forged(Publishable("1.5.0", CkAttributeAccessDto.Hidden), (RecordKey, "first"), (RecordKey, "second"));

        var result = await _h.EvaluateAsync(model);

        Assert.Equal(["OCTO-CK206"], Codes(result));
        Assert.Contains("acknowledged more than once", result.Messages.Single(m => m.Code == "OCTO-CK206").Text);
    }

    [Fact]
    public async Task An_entry_for_a_change_that_needs_none_is_refused_with_CK204()
    {
        await SeedBaselineAsync();
        var added = Versioned("1.5.0", m => SemVerTestModels.GetEnum(m).Values.Add(
            new CkEnumValueDto { Key = 9, Name = "Standby" }));
        var ordinary = CkChangeKey.Of(Assert.Single(Classify(Versioned("1.4.0"), added)).Change);

        var result = await _h.EvaluateAsync(await FromJsonAsync(added, [(ordinary, "adds a state")]));

        Assert.Equal(["OCTO-CK204"], Codes(result));
    }

    [Fact]
    public async Task An_entry_for_another_element_is_refused_with_CK203_and_CK204()
    {
        await SeedBaselineAsync();
        var other = SecretKey.Replace("SerialNumber", "State");

        var result = await _h.EvaluateAsync(await FromJsonAsync(Versioned("1.5.0", Tighten), [(other, "wrong element")]));

        Assert.Equal(["OCTO-CK203", "OCTO-CK204"], Codes(result));
    }

    [Fact]
    public async Task A_model_without_acknowledgements_is_published_with_the_same_messages_as_before()
    {
        await SeedBaselineAsync();

        var result = await _h.EvaluateAsync(Versioned("1.5.0"));

        Assert.Equal(CkPublishGateDecision.Proceed, result.Decision);
        Assert.Single(result.Messages);
        Assert.StartsWith("Compatibility: model", result.Messages[0].Text);
    }

    [Fact]
    public async Task A_ckLanguage_1_model_with_a_forged_section_gets_no_new_output_and_no_new_failure()
    {
        await _h.SeedAsync(GateModels.Create("2.4.0", ckLanguage: null));
        var model = Forged(GateModels.Create("2.5.0", "A,B,C", ckLanguage: null), ("*", ""), ("*", ""));

        var result = await _h.EvaluateAsync(model);

        Assert.Equal(CkPublishGateDecision.Proceed, result.Decision);
        Assert.Single(result.Messages);
    }

    [Fact]
    public async Task The_publish_gate_gives_the_verdict_of_ValidateVersion_and_the_compile_gate_for_the_same_corpus()
    {
        await SeedBaselineAsync();
        var corpus = new (CkCompiledModelRoot Model, string? Reason)[]
        {
            (Versioned("1.5.0", Tighten), null),
            (Versioned("1.5.0", Tighten), "accepted"),
            (Versioned("1.5.0"), "left over")
        };
        var compileGate = _h.Get<CkCompileGate>();

        foreach (var (model, reason) in corpus)
        {
            var forged = await FromJsonAsync(model, reason == null ? null : [(SecretKey, reason)]);
            var publish = await _h.EvaluateAsync(forged);
            var compile = await compileGate.RunAsync(forged, CkBaselineSource.Remote);

            Assert.Equal(
                compile.Messages.Where(m => m.Severity == CkCompileGateSeverity.Error).Select(m => m.Code).ToArray(),
                Codes(publish));
        }
    }
}
