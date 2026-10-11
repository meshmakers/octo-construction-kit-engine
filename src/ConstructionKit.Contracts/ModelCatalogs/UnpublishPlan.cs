namespace Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;

/// <summary>
///     What unpublishing a model version from a catalog would do (AB#5433): the dry run of
///     <c>octo-ckc -c Unpublish</c>. Read-only.
/// </summary>
public class UnpublishPlan
{
    /// <summary>The model version to unpublish.</summary>
    public required CkModelId ModelId { get; init; }

    /// <summary>The catalog the version would be removed from.</summary>
    public required string CatalogName { get; init; }

    /// <summary>True when the version is published in the catalog, false when it is not (never published or already unpublished).</summary>
    public required bool IsPublished { get; init; }

    /// <summary>
    ///     Published models of the same catalog that would be left with an unresolvable dependency: an exact pin
    ///     on the version, or a range the version is the only satisfying version of. Non-empty means the unpublish
    ///     is refused (also with force).
    /// </summary>
    public IReadOnlyList<UnpublishDependent> Dependents { get; init; } = [];
}

/// <summary>
///     A published model version that depends on a version about to be unpublished.
/// </summary>
/// <param name="ModelId">The dependent model version.</param>
/// <param name="Range">The dependency range of the dependent that only the unpublished version satisfies.</param>
public record UnpublishDependent(CkModelId ModelId, string Range);
