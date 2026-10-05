using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;

/// <summary>
///     Result of resolving the Secret attributes of a set of CK models.
/// </summary>
/// <param name="SecretAttributeIds">Normalised ids (<see cref="BlueprintSeedSecretLint.NormaliseAttributeId(string?)" />)</param>
/// <param name="UnresolvedModels">Dependencies that could not be resolved from any catalog</param>
public sealed record BlueprintSecretAttributeResolution(
    ISet<string> SecretAttributeIds,
    IReadOnlyList<string> UnresolvedModels);

/// <summary>
///     Resolves which attributes of a blueprint's CK model dependencies (transitively) are Secret
///     attributes, for <see cref="BlueprintSeedSecretLint" /> (AB#5528, decision 9).
/// </summary>
public interface IBlueprintSecretAttributeResolver
{
    /// <summary>
    ///     Resolves the Secret attributes of <paramref name="dependencies" /> and their transitive
    ///     dependencies (highest catalog version satisfying each range).
    /// </summary>
    Task<BlueprintSecretAttributeResolution> ResolveAsync(IEnumerable<CkModelIdVersionRange> dependencies,
        CancellationToken cancellationToken = default);
}
