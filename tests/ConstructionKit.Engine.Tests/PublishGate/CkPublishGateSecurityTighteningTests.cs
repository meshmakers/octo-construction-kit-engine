using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using static Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer.Rows.RowTestSupport;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.PublishGate;

/// <summary>
///     AB#6585: the security exception of row T7 (access tightened on a securitySensitive attribute: Minor +
///     acknowledge) holds only for an attribute that was sensitive in every published version of its major. The
///     two-step "mark in one Minor, tighten in the next" is a Major. The publish gate, the compile gate and
///     <c>ValidateVersion</c> share <see cref="CkCompatibilityVerdictService" />, so the publish gate stands for all
///     three. Against an in-memory catalog (operating rule O1: no shared catalog).
/// </summary>
public sealed class CkPublishGateSecurityTighteningTests : IDisposable
{
    private readonly GateHarness _h = new();

    public void Dispose() => _h.Dispose();

    private static string[] Codes(CkPublishGateResult r) => GateHarness.ErrorCodes(r);

    private static CkCompiledModelRoot Record(string version, bool sensitive, CkAttributeAccessDto? access = null,
        params (string Change, string Reason)[] acknowledge) =>
        Record(version, 2, sensitive, access, acknowledge);

    private static CkCompiledModelRoot Record(string version, int ckLanguage, bool sensitive,
        CkAttributeAccessDto? access = null, params (string Change, string Reason)[] acknowledge) => new()
    {
        ModelId = new CkModelId(M, version),
        Description = "Publishable fixture",
        CkLanguage = ckLanguage,
        Attributes = [new CkAttributeDto { AttributeId = "Secret", ValueType = AttributeValueTypesDto.String, SecuritySensitive = sensitive }],
        Records =
        [
            new CkRecordDto
            {
                RecordId = "Login",
                // The ckLanguage 2 default is Model; adopting it silently is a Major (derivable row), so a classic
                // model declares Any in the release that moves it.
                Derivable = ckLanguage >= 2 ? CkDerivableDto.Any : null,
                Attributes =
                [
                    new CkTypeAttributeDto
                    {
                        CkAttributeId = $"{M}-{version}/Secret", AttributeName = "Secret", IsOptional = true,
                        Access = access
                    }
                ]
            }
        ],
        Compatibility = acknowledge.Length == 0
            ? null
            : new CkCompatibilityDto
            {
                Acknowledge = acknowledge.Select(a => new CkAcknowledgeDto { Change = a.Change, Reason = a.Reason }).ToList()
            }
    };

    private const string AccessKey = "RecordAttribute:Login-1/Secret#Modified:access";

    [Fact]
    public async Task Marking_in_one_Minor_and_tightening_in_the_next_is_refused_with_CK200_also_with_an_acknowledgement()
    {
        await _h.SeedAsync(Record("1.0.0", false));
        await _h.SeedAsync(Record("1.1.0", true)); // step 1: the marker alone is a Minor, no acknowledgement

        var withAck = await _h.EvaluateAsync(Record("1.2.0", true, CkAttributeAccessDto.ReadOnly,
            (AccessKey, "Close accepted risk R13")));
        var withoutAck = await _h.EvaluateAsync(Record("1.2.0", true, CkAttributeAccessDto.ReadOnly));

        Assert.Equal(CkPublishGateDecision.Refused, withAck.Decision);
        // The Major break is not waivable; the entry names a change that needs no acknowledgement and is stale.
        Assert.Equal(["OCTO-CK200", "OCTO-CK204"], Codes(withAck));
        var tooLow = withAck.Messages.Single(m => m.Code == "OCTO-CK200").Text;
        Assert.Contains("MAJOR", tooLow);
        Assert.Contains("became securitySensitive within this major line", tooLow);
        Assert.Contains("not sensitive in 1.0.0", tooLow);
        Assert.Equal(["OCTO-CK200"], Codes(withoutAck));
    }

    [Fact]
    public async Task The_same_tightening_is_accepted_as_a_Major()
    {
        await _h.SeedAsync(Record("1.0.0", false));
        await _h.SeedAsync(Record("1.1.0", true));

        var result = await _h.EvaluateAsync(Record("2.0.0", true, CkAttributeAccessDto.ReadOnly));

        Assert.Equal(CkPublishGateDecision.Proceed, result.Decision);
    }

    [Fact]
    public async Task Setting_the_marker_on_its_own_stays_a_Minor_without_an_acknowledgement()
    {
        await _h.SeedAsync(Record("1.0.0", false));

        var result = await _h.EvaluateAsync(Record("1.1.0", true));

        Assert.Equal(CkPublishGateDecision.Proceed, result.Decision);
    }

    [Fact]
    public async Task A_tightening_of_an_attribute_that_was_sensitive_from_the_start_is_a_Minor_with_an_acknowledgement()
    {
        await _h.SeedAsync(Record("1.0.0", true));
        await _h.SeedAsync(Record("1.1.0", true));

        var missing = await _h.EvaluateAsync(Record("1.2.0", true, CkAttributeAccessDto.ReadOnly));
        var acknowledged = await _h.EvaluateAsync(Record("1.2.0", true, CkAttributeAccessDto.ReadOnly,
            (AccessKey, "Close accepted risk R13")));

        Assert.Equal(["OCTO-CK203"], Codes(missing));
        Assert.Equal(CkPublishGateDecision.Proceed, acknowledged.Decision);
        Assert.Single(acknowledged.Verdict!.Acknowledgement.Acknowledged);
    }

    [Fact]
    public async Task A_marker_cleared_and_set_again_inside_the_major_does_not_qualify()
    {
        await _h.SeedAsync(Record("1.0.0", true));
        await _h.SeedAsync(Record("1.1.0", false));
        await _h.SeedAsync(Record("1.2.0", true));

        var result = await _h.EvaluateAsync(Record("1.3.0", true, CkAttributeAccessDto.ReadOnly));

        Assert.Equal(["OCTO-CK200"], Codes(result));
        Assert.Contains("not sensitive in 1.1.0", result.Messages.Single(m => m.Code == "OCTO-CK200").Text);
    }

    [Fact]
    public async Task The_history_of_an_earlier_major_does_not_count()
    {
        await _h.SeedAsync(Record("1.0.0", false));
        await _h.SeedAsync(Record("2.0.0", true));

        var result = await _h.EvaluateAsync(Record("2.1.0", true, CkAttributeAccessDto.ReadOnly,
            (AccessKey, "Close accepted risk R13")));

        Assert.Equal(CkPublishGateDecision.Proceed, result.Decision);
    }

    // ── AB#7771 (G1 = B): the one release that moves a classic model to ckLanguage 2 ─────────────────────────────

    [Fact]
    public async Task First_ckLanguage2_release_may_hide_a_sensitive_attribute_as_Minor_with_an_acknowledgement()
    {
        await _h.SeedAsync(Record("1.0.0", 1, false));

        var missing = await _h.EvaluateAsync(Record("1.1.0", true, CkAttributeAccessDto.Hidden));
        var acknowledged = await _h.EvaluateAsync(Record("1.1.0", true, CkAttributeAccessDto.Hidden,
            (AccessKey, "Close credential exposure R13")));

        Assert.Equal(["OCTO-CK203"], Codes(missing));
        Assert.Equal(CkPublishGateDecision.Proceed, acknowledged.Decision);
        Assert.Single(acknowledged.Verdict!.Acknowledgement.Acknowledged);
    }

    [Fact]
    public async Task First_ckLanguage2_release_still_needs_Major_for_an_attribute_not_marked_sensitive()
    {
        await _h.SeedAsync(Record("1.0.0", 1, false));

        var result = await _h.EvaluateAsync(Record("1.1.0", false, CkAttributeAccessDto.Hidden,
            (AccessKey, "Close credential exposure R13")));

        Assert.Contains("OCTO-CK200", Codes(result));
    }

    [Fact]
    public async Task A_second_ckLanguage2_release_cannot_use_the_exception_again()
    {
        await _h.SeedAsync(Record("1.0.0", 1, false));
        await _h.SeedAsync(Record("1.1.0", 2, false)); // the first ckLanguage 2 release, attribute not yet marked

        var result = await _h.EvaluateAsync(Record("1.2.0", true, CkAttributeAccessDto.Hidden,
            (AccessKey, "Close credential exposure R13")));

        Assert.Contains("OCTO-CK200", Codes(result));
    }

    [Fact]
    public async Task The_exception_is_once_per_model_also_after_dropping_back_to_ckLanguage1_in_a_later_major()
    {
        await _h.SeedAsync(Record("1.0.0", 2, false)); // the model was ckLanguage 2 once
        await _h.SeedAsync(Record("2.0.0", 1, false)); // dropped back in a Major

        var result = await _h.EvaluateAsync(Record("2.1.0", true, CkAttributeAccessDto.Hidden,
            (AccessKey, "Close credential exposure R13")));

        Assert.Contains("OCTO-CK200", Codes(result));
    }

    [Fact]
    public async Task Widening_a_method_role_needs_an_acknowledgement()
    {
        var baseline = Model();
        baseline.ModelId = new CkModelId(M, "1.0.0");
        await _h.SeedAsync(baseline);
        var widened = Model();
        widened.ModelId = new CkModelId(M, "1.1.0");
        Method(widened, "type").Authorization!.Roles = ["UserManagement", "Everyone"];
        var key = CkChangeKey.Of(Assert.Single(Classify(baseline, widened), c => c.Change.Property == "roles").Change);

        var missing = await _h.EvaluateAsync(widened);
        var acknowledged = Model();
        acknowledged.ModelId = new CkModelId(M, "1.1.0");
        Method(acknowledged, "type").Authorization!.Roles = ["UserManagement", "Everyone"];
        acknowledged.Compatibility = new CkCompatibilityDto
        {
            Acknowledge = [new CkAcknowledgeDto { Change = key, Reason = "Everyone may read the status; no data leaves" }]
        };
        var accepted = await _h.EvaluateAsync(acknowledged);

        Assert.Equal(["OCTO-CK203"], Codes(missing));
        Assert.Contains($"\"{key}\"", missing.Messages.Single(m => m.Code == "OCTO-CK203").Text);
        Assert.Equal(CkPublishGateDecision.Proceed, accepted.Decision);
    }
}
