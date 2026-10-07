using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.RangeRetention;

namespace Meshmakers.Octo.ConstructionKit.SourceGeneration;

/// <summary>
///     D1 (CK v2 Phase 0): a range-retaining compiled model references its dependencies major-qualified
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
}
