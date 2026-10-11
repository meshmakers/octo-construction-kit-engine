using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     One refusal, one text (AB#6389). Every reason the publish gate finds for one publish is reported together, in a
///     fixed order, without duplicates, and each reason is a self-contained line that starts with the name of the gate
///     that refused, names the model and the target catalog and ends with the next step. <c>ckc Publish</c> and the
///     MSBuild publish of <c>CkCompile</c> print the same lines because both get them from
///     <c>CatalogService.PublishAsync</c> (through the <see cref="OperationResult" /> of the refusal exception).
/// </summary>
/// <remarks>
///     The O1 guard (operating rule O1) is a pipeline step and never reaches the engine: it uses the same shape with its
///     own gate name (<c>[O1 guard]</c>), and a model it refuses never gets here. The gate name of every reason in this
///     class is <see cref="CompatGate" />; texts of the one gate never mention the other.
/// </remarks>
public static class CkPublishRefusal
{
    /// <summary>The gate name that starts every reason of the engine publish gate.</summary>
    public const string CompatGate = "compat gate";

    /// <summary>The code of a version number that was unpublished and is burned (AB#5433, reported by the gate).</summary>
    public const string BurnedVersionCode = "OCTO-CK207";

    // Most fundamental first: the version number is unusable (207, 205), the baseline is unknown (202), the declared
    // version is too low (201, 200), then the findings about the acknowledgements (206, 203, 204).
    private static readonly string[] CodeOrder =
    [
        BurnedVersionCode, "OCTO-CK205", "OCTO-CK202", "OCTO-CK201", "OCTO-CK200", "OCTO-CK206", "OCTO-CK203",
        "OCTO-CK204"
    ];

    /// <summary>
    ///     Puts the messages of one publish into their final shape: information and warnings first, in the order they were
    ///     produced, then the errors ordered by <see cref="CodeOrder" /> (stable inside one code) and without repeated
    ///     (code, text) pairs.
    /// </summary>
    public static IReadOnlyList<CkCompileGateMessage> Normalize(IEnumerable<CkCompileGateMessage> messages)
    {
        var all = messages.ToList();
        var seen = new HashSet<(string?, string)>();
        var errors = all.Where(m => m.Severity == CkCompileGateSeverity.Error)
            .Where(m => seen.Add((m.Code, m.Text)))
            .Select((message, index) => (message, index))
            .OrderBy(x => Rank(x.message.Code))
            .ThenBy(x => x.index)
            .Select(x => x.message);
        return all.Where(m => m.Severity != CkCompileGateSeverity.Error).Concat(errors).ToList();
    }

    /// <summary>The self-contained line of one reason.</summary>
    public static string FormatReason(string catalogName, CkModelId modelId, CkCompileGateMessage reason)
    {
        var code = reason.Code ?? "OCTO-CK2xx";
        var next = GetNextStep(reason.Code);
        return $"[{CompatGate}] {code} (model '{modelId.FullName}', catalog '{catalogName}'): {reason.Text}" +
               (next == null ? "" : $" Next step: {next}");
    }

    /// <summary>The one-line summary used as the text of the exception.</summary>
    public static string FormatSummary(string catalogName, CkModelId modelId, IReadOnlyList<CkCompileGateMessage> errors)
    {
        var codes = string.Join(", ", errors.Select(e => e.Code ?? "OCTO-CK2xx").Distinct());
        return $"Construction kit model '{modelId.FullName}' was NOT published to catalog '{catalogName}': refused by " +
               $"the {CompatGate} with {errors.Count} reason(s) ({codes}). Nothing was written. Every reason follows " +
               "with its next step, the most fundamental first.";
    }

    /// <summary>What the publisher does about the finding; null for an unknown code.</summary>
    public static string? GetNextStep(string? code) => code switch
    {
        BurnedVersionCode => "publish the next free version number; a withdrawn number is never published again.",
        "OCTO-CK205" =>
            "raise the version in ckModel.yaml and publish again; a published version is immutable (also with --replace).",
        "OCTO-CK202" =>
            "check the catalog connectivity and access tokens and retry; the gate is never skipped.",
        "OCTO-CK201" => "declare a version above the baseline version of the same major.",
        "OCTO-CK200" =>
            "raise the version in ckModel.yaml to the minimum named in this reason (`octo-ckc -c ValidateVersion -p <model folder> --apply` writes it), or revert the change.",
        "OCTO-CK206" => "recompile the model from a valid ckModel.yaml; the compiled file is not edited by hand.",
        "OCTO-CK203" =>
            "add the entry shown to compatibility.acknowledge in ckModel.yaml, with a reason, or revert the change.",
        "OCTO-CK204" => "remove the entry from compatibility.acknowledge; it is valid for one release only.",
        _ => null
    };

    private static int Rank(string? code)
    {
        var index = code == null ? -1 : Array.IndexOf(CodeOrder, code);
        return index < 0 ? CodeOrder.Length : index;
    }
}
