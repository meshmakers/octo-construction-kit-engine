using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     The shape of a change the diff can emit: element kind, change kind and (for modifications) the
///     property name. Values and element ids are not part of the shape. The classification guard test
///     classifies one synthetic change per shape of <see cref="CkModelDiffService.EmittableChanges" /> and fails
///     when a shape reaches the classifier's defensive default (AB#6272).
/// </summary>
/// <param name="ElementKind">The element kind of the change.</param>
/// <param name="ChangeKind">Added, removed or modified.</param>
/// <param name="Property">The diffed property for <see cref="CkModelChangeKind.Modified" />, otherwise null.</param>
public readonly record struct CkModelChangeShape(
    CkModelElementKind ElementKind,
    CkModelChangeKind ChangeKind,
    string? Property = null)
{
    /// <summary>
    ///     The shape of a concrete change.
    /// </summary>
    public static CkModelChangeShape Of(CkModelChange change) =>
        new(change.ElementKind, change.ChangeKind, change.ChangeKind == CkModelChangeKind.Modified ? change.Property : null);

    /// <inheritdoc />
    public override string ToString() =>
        Property == null ? $"{ElementKind} {ChangeKind}" : $"{ElementKind} {ChangeKind} '{Property}'";
}
