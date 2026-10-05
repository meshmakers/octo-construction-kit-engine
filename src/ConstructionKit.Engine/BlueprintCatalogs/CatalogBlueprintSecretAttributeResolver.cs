using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;

/// <summary>
///     <see cref="IBlueprintSecretAttributeResolver" /> over the CK model catalogs: each dependency
///     range resolves to the highest version a catalog holds, the compiled model contributes its
///     Secret attributes, and its exact dependency pins are followed.
/// </summary>
internal class CatalogBlueprintSecretAttributeResolver : IBlueprintSecretAttributeResolver
{
    private readonly ICatalogManager _catalogManager;
    private readonly ILogger<CatalogBlueprintSecretAttributeResolver> _logger;

    public CatalogBlueprintSecretAttributeResolver(ICatalogManager catalogManager,
        ILogger<CatalogBlueprintSecretAttributeResolver> logger)
    {
        _catalogManager = catalogManager;
        _logger = logger;
    }

    public async Task<BlueprintSecretAttributeResolution> ResolveAsync(
        IEnumerable<CkModelIdVersionRange> dependencies, CancellationToken cancellationToken = default)
    {
        var secretAttributeIds = new HashSet<string>(StringComparer.Ordinal);
        var unresolved = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<CkModelId>();

        foreach (var range in dependencies)
        {
            if (range.IsEmpty || range.Name.StartsWith("$", StringComparison.Ordinal))
            {
                continue;
            }

            ModelExistingResult existing;
            try
            {
                existing = await _catalogManager.IsExistingAsync(range).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Secret seed lint: resolving {Dependency} failed", range.FullName);
                unresolved.Add(range.FullName);
                continue;
            }

            if (!existing.Exists || existing.ModelId == null)
            {
                unresolved.Add(range.FullName);
                continue;
            }

            pending.Enqueue(existing.ModelId);
        }

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var modelId = pending.Dequeue();
            if (!visited.Add(modelId.FullName))
            {
                continue;
            }

            CkCompiledModelRoot? model;
            try
            {
                model = await _catalogManager.TryGetAsync(modelId, new OperationResult(), null, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Secret seed lint: loading {ModelId} failed", modelId.FullName);
                model = null;
            }

            if (model == null)
            {
                unresolved.Add(modelId.FullName);
                continue;
            }

            foreach (var attribute in model.Attributes ?? [])
            {
                if (attribute.ValueType == AttributeValueTypesDto.Secret)
                {
                    secretAttributeIds.Add(BlueprintSeedSecretLint.NormaliseAttributeId(modelId.Name,
                        attribute.AttributeId));
                }
            }

            foreach (var dependency in model.Dependencies ?? [])
            {
                pending.Enqueue(dependency);
            }
        }

        return new BlueprintSecretAttributeResolution(secretAttributeIds, unresolved);
    }
}
