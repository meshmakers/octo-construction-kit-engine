namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Shared rule for the <see cref="RtCkId{T}"/> JSON "virtual property" shim.
/// </summary>
/// <remarks>
/// On the wire an <see cref="RtCkId{T}"/> serialises to a bare <see cref="RtCkId{T}.SemanticVersionedFullName"/>
/// string (see <c>RtCkIdConverter.Write</c>), but legacy pipeline YAML drills
/// <c>.SemanticVersionedFullName</c> / <c>.FullName</c> expecting the historical object shape.
/// Readers therefore treat those two property names on a JSON string as virtual self-aliases.
/// This rule was previously hand-reimplemented as magic strings in three SDK walkers
/// (JsonPathWalker, DataOverlay, LayeredSource) plus two structured-object read-back sites
/// (RtNewtonsoftAttributesConverter, CreateUpdateInfoNode); it is centralised here — next to
/// <see cref="RtCkId{T}"/> where the wire shape is decided — so all consumers share one definition.
/// The names are the stable wire contract (pinned by <c>RtCkIdJsonShimTests</c>).
/// </remarks>
public static class RtCkIdJsonShim
{
    /// <summary>The semantic (versioned) identifier property name carried on the wire.</summary>
    public const string SemanticVersionedFullNameKey = "SemanticVersionedFullName";

    /// <summary>The unversioned identifier property name.</summary>
    public const string FullNameKey = "FullName";

    /// <summary>
    /// True when <paramref name="propertyName"/> is one of the RtCkId virtual property names
    /// (<see cref="SemanticVersionedFullNameKey"/> / <see cref="FullNameKey"/>). Allocation-free
    /// ordinal comparison; callers keep their own "current node is a JSON string" check.
    /// </summary>
    public static bool IsVirtualProperty(string propertyName) =>
        string.Equals(propertyName, SemanticVersionedFullNameKey, System.StringComparison.Ordinal) ||
        string.Equals(propertyName, FullNameKey, System.StringComparison.Ordinal);
}
