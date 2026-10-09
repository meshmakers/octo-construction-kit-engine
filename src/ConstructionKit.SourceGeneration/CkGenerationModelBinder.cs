using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.RangeRetention;

namespace Meshmakers.Octo.ConstructionKit.SourceGeneration;

/// <summary>
///     D1 (CK v2): a range-retaining compiled model references its dependencies major-qualified
///     (<c>System@2/Enabled-1</c>) while the compile cache the generator restores holds the concrete versions the
///     model was resolved against (<c>System-2.5.0/Enabled-1</c>). Binds the references to those versions before
///     any per-element generator looks them up, so generation sees exactly what a classic model provides.
/// </summary>
internal static class CkGenerationModelBinder
{
    /// <summary>
    ///     Binds major-qualified references of <paramref name="compiledModel" /> (in place — it was just
    ///     deserialized for this generation run) to the models of the restored cache.
    /// </summary>
    /// <returns>The number of bound references.</returns>
    public static int BindToCache(CkCompiledModelRoot compiledModel, ICkCacheService cacheService, string tenantId)
    {
        return CkReferenceRewriter.HasMajorQualifiedReferences(compiledModel)
            ? CkReferenceRewriter.BindMajorQualified(compiledModel, cacheService.GetCkModelIds(tenantId))
            : 0;
    }

    /// <summary>
    ///     F1.1-S2 (D1 diagnostic): the major-qualified references that are still unbound after
    ///     <see cref="BindToCache" />, as <c>kind Model@N/Element</c>.
    /// </summary>
    public static IReadOnlyList<string> FindUnbound(CkCompiledModelRoot compiledModel)
    {
        return CkReferenceRewriter.CollectReferences(compiledModel)
            .Where(r => r.ModelId.IsMajorQualified)
            .Select(r => $"{r.Kind} {r.ModelId.FullName}/{r.ElementId}")
            .Distinct()
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    ///     Message of the OM1004 diagnostic: names the unbound references and the versions the compile cache holds,
    ///     instead of the opaque "not found in CkCache" exception of the per-element generators.
    /// </summary>
    public static string DescribeUnbound(CkCompiledModelRoot compiledModel, IReadOnlyList<string> unbound,
        IEnumerable<Contracts.CkModelId> cachedModels)
    {
        var cached = string.Join(", ", cachedModels.Select(m => m.FullName).OrderBy(m => m, StringComparer.Ordinal));
        return $"Model '{compiledModel.ModelId}' references {string.Join(", ", unbound)}, but the compile cache " +
               $"holds no version of that model and major (cache: {(cached.Length == 0 ? "empty" : cached)}). The " +
               "dependency was resolved in another major or is missing from the compile cache; check the dependency " +
               "range in ckModel.yaml and rebuild the model.";
    }
}
