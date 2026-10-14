using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     Outcome of <see cref="CkPublishEquivalence.CompareAsync" />.
/// </summary>
/// <param name="AreEquivalent">True when both compiled models are the same after canonicalisation.</param>
/// <param name="DifferenceClasses">
///     The classes of difference that were normalised away (empty when the models are byte-identical or when they
///     differ in content). Each entry is a short human-readable phrase for the log.
/// </param>
public sealed record CkPublishEquivalenceResult(bool AreEquivalent, IReadOnlyList<string> DifferenceClasses);

/// <summary>
///     AB#6386: semantic comparison of a compiled model with the already published one, used by the publish gate's
///     same-version rule. A published version is immutable, but a newer compiler (or a source file that restates a
///     default) may legitimately produce a different file for the same model content. Both sides are brought into one
///     canonical form through the same deserialise -> normalise -> serialise path; only what survives the
///     normalisation counts as a content change (<c>OCTO-CK205</c>).
/// </summary>
/// <remarks>
///     The normalisations are an explicit allow-list, one named class each. Anything not listed is content:
///     <list type="bullet">
///         <item><b>restated default ownership</b>: <c>ownership: SeedOwned</c> / <c>RuntimeState</c> on an attribute
///             definition (or on an assignment whose definition is in the same model) that equals what the deprecated
///             <c>isRuntimeState</c> alias resolves to. Resolved ownership is unchanged, so no consumer can tell.
///             <c>TenantOwned</c> and <c>Secret</c> are never normalised (a legacy reader sees them as runtime state, so
///             adding or removing them is a real change).</item>
///         <item><b>element order</b>: the order of the top-level lists (types, attributes, enums, records, association
///             roles, interfaces, dependencies) carries no meaning.</item>
///         <item><b>compiler-stamped metadata</b>: <c>minEngineVersion</c> is derived by the compiler from the features
///             the model uses; a content change that needs a newer engine shows up in the content itself.</item>
///     </list>
///     Adding a class: add a step to <see cref="Steps" /> with a test that changes only that aspect (equivalent) and one
///     that changes content next to it (still <c>OCTO-CK205</c>).
/// </remarks>
public sealed class CkPublishEquivalence
{
    private readonly ICkJsonSerializer _serializer;

    /// <summary>
    ///     Creates a new instance of the <see cref="CkPublishEquivalence" /> class.
    /// </summary>
    public CkPublishEquivalence(ICkJsonSerializer serializer)
    {
        _serializer = serializer;
    }

    private static readonly (string Name, Func<CkCompiledModelRoot, CkCompiledModelRoot, bool> Apply)[] Steps =
    [
        ("restated default ownership (ownership: SeedOwned/RuntimeState equals the isRuntimeState default)",
            NormalizeRestatedOwnership),
        ("element order", NormalizeOrder),
        ("compiler-stamped metadata (minEngineVersion)", NormalizeCompilerStampedMetadata),
        ("resolved dependency closure of a range-retaining model (moved inside the declared ranges)",
            NormalizeResolvedClosure)
    ];

    /// <summary>
    ///     Compares the published model with the model about to be published.
    /// </summary>
    public async Task<CkPublishEquivalenceResult> CompareAsync(CkCompiledModelRoot published, CkCompiledModelRoot current)
    {
        var publishedText = await SerializeAsync(published).ConfigureAwait(false);
        var currentText = await SerializeAsync(current).ConfigureAwait(false);
        if (string.Equals(publishedText, currentText, StringComparison.Ordinal))
        {
            return new CkPublishEquivalenceResult(true, []);
        }

        // Work on copies: the caller keeps using both models (the publish writes `current` unchanged).
        var a = await CloneAsync(publishedText).ConfigureAwait(false);
        var b = await CloneAsync(currentText).ConfigureAwait(false);

        var applied = new List<string>();
        foreach (var (name, apply) in Steps)
        {
            if (apply(a, b))
            {
                applied.Add(name);
            }
        }

        var equivalent = string.Equals(await SerializeAsync(a).ConfigureAwait(false),
            await SerializeAsync(b).ConfigureAwait(false), StringComparison.Ordinal);
        return new CkPublishEquivalenceResult(equivalent, equivalent ? applied : []);
    }

    private async Task<string> SerializeAsync(CkCompiledModelRoot model)
    {
        using var stream = new MemoryStream();
        var writer = new StreamWriter(stream);
        await _serializer.SerializeAsync(writer, model).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private async Task<CkCompiledModelRoot> CloneAsync(string text)
    {
        var result = new OperationResult();
        var clone = await _serializer.DeserializeCompiledModelRootAsync(text, "publish-equivalence", result, true)
            .ConfigureAwait(false);
        if (result.HasErrors || result.HasFatalErrors)
        {
            throw new InvalidOperationException(
                "A compiled model that was just serialised could not be read back: " +
                string.Join("; ", result.Messages.Select(m => m.MessageText)));
        }

        return clone;
    }

    // ---- restated default ownership ------------------------------------------------------------------------------

    private static bool NormalizeRestatedOwnership(CkCompiledModelRoot a, CkCompiledModelRoot b) =>
        NormalizeRestatedOwnership(a) | NormalizeRestatedOwnership(b);

    private static bool NormalizeRestatedOwnership(CkCompiledModelRoot model)
    {
        var changed = false;

        // Effective ownership of every definition of this model, before anything is rewritten.
        var definitions = new Dictionary<string, AttributeOwnershipDto>(StringComparer.Ordinal);
        foreach (var attribute in model.Attributes ?? [])
        {
            definitions[attribute.AttributeId.FullName] =
                AttributeOwnership.Resolve(attribute.Ownership, attribute.IsRuntimeState);
        }

        // Assignments first (they are judged against the definition as written).
        foreach (var assignment in AllAssignments(model))
        {
            // Only a definition of this very model is known here; assignments of a dependency's attribute stay as they are.
            if (assignment.Ownership.HasValue &&
                assignment.CkAttributeId.ModelId.Equals(model.ModelId) &&
                definitions.TryGetValue(assignment.CkAttributeId.ElementId.FullName, out var inherited) &&
                assignment.Ownership.Value == inherited)
            {
                assignment.Ownership = null;
                changed = true;
            }
        }

        foreach (var attribute in model.Attributes ?? [])
        {
            if (!attribute.Ownership.HasValue)
            {
                continue;
            }

            var mirror = attribute.IsRuntimeState;
            if (attribute.Ownership.Value == AttributeOwnership.Resolve(null, mirror))
            {
                attribute.Ownership = null;
                attribute.IsRuntimeState = mirror;
                changed = true;
            }
        }

        return changed;
    }

    private static IEnumerable<CkTypeAttributeDto> AllAssignments(CkCompiledModelRoot model)
    {
        foreach (var type in model.Types ?? [])
        {
            foreach (var assignment in type.Attributes ?? [])
            {
                yield return assignment;
            }
        }

        foreach (var record in model.Records ?? [])
        {
            foreach (var assignment in record.Attributes ?? [])
            {
                yield return assignment;
            }
        }
    }

    // ---- element order -------------------------------------------------------------------------------------------

    private static bool NormalizeOrder(CkCompiledModelRoot a, CkCompiledModelRoot b)
    {
        // Reported only when the two sides really listed their elements in a different order.
        var differed = OrderSignature(a) != OrderSignature(b);
        SortTopLevel(a);
        SortTopLevel(b);
        return differed;
    }

    private static string OrderSignature(CkCompiledModelRoot model) => string.Join("\n", new[]
    {
        string.Join("|", (model.Types ?? []).Select(t => t.TypeId.ToString())),
        string.Join("|", (model.Attributes ?? []).Select(t => t.AttributeId.ToString())),
        string.Join("|", (model.Enums ?? []).Select(t => t.EnumId.ToString())),
        string.Join("|", (model.Records ?? []).Select(t => t.RecordId.ToString())),
        string.Join("|", (model.AssociationRoles ?? []).Select(t => t.AssociationRoleId.ToString())),
        string.Join("|", (model.Interfaces ?? []).Select(t => t.InterfaceId.ToString())),
        string.Join("|", (model.Dependencies ?? []).Select(t => t.ToString()))
    });

    private static void SortTopLevel(CkCompiledModelRoot model)
    {
        Sort(model.Types, t => t.TypeId.ToString());
        Sort(model.Attributes, t => t.AttributeId.ToString());
        Sort(model.Enums, t => t.EnumId.ToString());
        Sort(model.Records, t => t.RecordId.ToString());
        Sort(model.AssociationRoles, t => t.AssociationRoleId.ToString());
        Sort(model.Interfaces, t => t.InterfaceId.ToString());
        Sort(model.Dependencies, t => t.ToString());
    }

    private static bool Sort<T>(List<T>? list, Func<T, string> key)
    {
        if (list == null || list.Count < 2)
        {
            return false;
        }

        var sorted = list.OrderBy(key, StringComparer.Ordinal).ToList();
        if (sorted.SequenceEqual(list))
        {
            return false;
        }

        list.Clear();
        list.AddRange(sorted);
        return true;
    }

    // ---- resolved closure of a range-retaining model ------------------------------------------------------------------

    /// <summary>
    ///     AB#5435 (class 4): the <c>dependencies</c> closure of a range-retaining model is what the build resolved, not
    ///     part of its surface; a rebuild after a dependency minor legitimately writes other versions into it. Normalised
    ///     away only when <see cref="CkClosureMove.IsWithinDeclaredRange" /> holds (same names, every difference a move up
    ///     inside the same major and the declared range, ranges and floors identical); any other closure difference stays
    ///     content. The published file is not touched: only the compared copies are.
    /// </summary>
    private static bool NormalizeResolvedClosure(CkCompiledModelRoot a, CkCompiledModelRoot b)
    {
        // Direction does not matter for the equivalence: published may be the newer or the older closure (a rebuild
        // against an older catalog). The predicate is asked in both directions.
        if (!CkClosureMove.IsWithinDeclaredRange(a, b) && !CkClosureMove.IsWithinDeclaredRange(b, a))
        {
            return false;
        }

        a.Dependencies = null;
        b.Dependencies = null;
        return true;
    }

    // ---- compiler-stamped metadata -------------------------------------------------------------------------------

    private static bool NormalizeCompilerStampedMetadata(CkCompiledModelRoot a, CkCompiledModelRoot b)
    {
        if (string.Equals(a.MinEngineVersion, b.MinEngineVersion, StringComparison.Ordinal))
        {
            return false;
        }

        a.MinEngineVersion = null;
        b.MinEngineVersion = null;
        return true;
    }
}
