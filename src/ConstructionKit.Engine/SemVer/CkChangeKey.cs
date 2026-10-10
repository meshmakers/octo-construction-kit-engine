using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     The stable key of a model change (AB#6295): what a <c>compatibility.acknowledge</c> entry refers to and what the
///     compile gate prints for the author to copy. Free of model version numbers, so it stays valid when only the version
///     is bumped, and deterministic.
/// </summary>
/// <remarks>
///     The element id is the id inside the model (<c>Login-1</c> is the record <c>Login</c> in revision 1 of the record;
///     the model name and the model version are not part of it). Format:
///     <c>{ElementKind}:{ElementId}#{ChangeKind}</c> for an added or removed element and
///     <c>{ElementKind}:{ElementId}#Modified:{property}</c> for a changed property, for example
///     <c>RecordAttribute:Login-1/Secret#Modified:access</c>. Type indexes have no identity of their own, so
///     their key carries the index definition: <c>TypeIndex:Machine-1/index#Added:UniqueNotDeleted on Serial</c>.
///     Matching is an exact, ordinal comparison; there are no wildcards.
/// </remarks>
public static class CkChangeKey
{
    /// <summary>
    ///     Returns the key of <paramref name="change" />.
    /// </summary>
    public static string Of(CkModelChange change)
    {
        var key = $"{change.ElementKind}:{change.ElementId}#{change.ChangeKind}";
        if (change.ChangeKind == CkModelChangeKind.Modified)
        {
            key = change.Property == null ? key : $"{key}:{change.Property}";
            // Several indexes of a type share one element id: the definitions tell them apart.
            return change.ElementKind == CkModelElementKind.TypeIndex
                ? $"{key}:{change.OldValue} -> {change.NewValue}"
                : key;
        }

        if (change.ElementKind == CkModelElementKind.TypeIndex)
        {
            var definition = change.ChangeKind == CkModelChangeKind.Added ? change.NewValue : change.OldValue;
            return definition == null ? key : $"{key}:{definition}";
        }

        return key;
    }

    /// <summary>
    ///     The YAML entry the author adds to <c>ckModel.yaml</c> to acknowledge <paramref name="change" />.
    /// </summary>
    public static string ExampleEntry(CkModelChange change) =>
        "compatibility:\n  acknowledge:\n" +
        $"    - change: \"{Of(change).Replace("\\", "\\\\").Replace("\"", "\\\"")}\"\n" +
        "      reason: \"<why this change is accepted>\"";
}
