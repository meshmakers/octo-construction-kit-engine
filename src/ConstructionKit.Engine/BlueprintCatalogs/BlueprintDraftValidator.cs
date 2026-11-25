using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YamlDotNet.RepresentationModel;

namespace Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;

/// <summary>
///     The blueprint draft validator (AB#9371). Every finding carries a stable code of the construction kit message
///     catalog (149-167); the texts of the former octo-bpm messages are unchanged.
/// </summary>
internal class BlueprintDraftValidator : IBlueprintDraftValidator
{
    /// <summary>File name of the blueprint manifest.</summary>
    public const string BlueprintMetaFileName = "blueprint.yaml";

    private const string SeedDataDirectory = "seed-data";

    private readonly IBlueprintSerializer _serializer;
    private readonly IBlueprintSecretAttributeResolver? _secretAttributeResolver;
    private readonly ICatalogManager? _catalogManager;
    private readonly ILogger _logger;

    /// <summary>
    ///     Creates the validator.
    /// </summary>
    /// <param name="serializer">Blueprint serializer</param>
    /// <param name="secretAttributeResolver">Resolver of the Secret attributes for the seed lint; without it the lint is skipped</param>
    /// <param name="catalogManager">CK model catalogs for the dependency-range check; without it the check is skipped</param>
    /// <param name="logger">Logger</param>
    public BlueprintDraftValidator(IBlueprintSerializer serializer,
        IBlueprintSecretAttributeResolver? secretAttributeResolver = null,
        ICatalogManager? catalogManager = null,
        ILogger<BlueprintDraftValidator>? logger = null)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _secretAttributeResolver = secretAttributeResolver;
        _catalogManager = catalogManager;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public async Task<BlueprintDraftValidationResult> ValidateAsync(IBlueprintFileSource source,
        BlueprintValidationOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        options ??= new BlueprintValidationOptions();
        var result = new OperationResult();

        string? content;
        try
        {
            content = await source.ReadTextAsync(BlueprintMetaFileName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            result.AddMessage(MessageCodes.BlueprintDraftUnreadable(BlueprintMetaFileName, ex.Message));
            return new BlueprintDraftValidationResult(null, result.Messages);
        }

        if (content == null)
        {
            result.AddMessage(MessageCodes.BlueprintManifestMissing(BlueprintMetaFileName, BlueprintMetaFileName));
            return new BlueprintDraftValidationResult(null, result.Messages);
        }

        BlueprintMetaRootDto blueprintMeta;
        try
        {
            blueprintMeta = _serializer.DeserializeBlueprintMeta(content, BlueprintMetaFileName, new OperationResult());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.AddMessage(MessageCodes.BlueprintManifestUnreadable(BlueprintMetaFileName,
                ex.InnerException?.Message ?? ex.Message));
            return new BlueprintDraftValidationResult(null, result.Messages);
        }

        if (blueprintMeta.BlueprintId == null || blueprintMeta.BlueprintId.IsEmpty)
        {
            result.AddMessage(MessageCodes.BlueprintIdMissing(BlueprintMetaFileName));
        }

        var seeds = await ValidateSeedDataAsync(source, blueprintMeta, result, cancellationToken).ConfigureAwait(false);

        if (options.LintSecretSeedValues)
        {
            await LintSecretSeedValuesAsync(blueprintMeta, seeds, result, cancellationToken).ConfigureAwait(false);
        }

        await ValidateCkModelDependenciesAsync(blueprintMeta, options, result, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("Blueprint draft {BlueprintId} validated: {Count} finding(s)", blueprintMeta.BlueprintId,
            result.Messages.Count);
        return new BlueprintDraftValidationResult(blueprintMeta, result.Messages);
    }

    private static async Task<List<(string SeedDataPath, YamlStream Yaml)>> ValidateSeedDataAsync(
        IBlueprintFileSource source, BlueprintMetaRootDto blueprintMeta, OperationResult result,
        CancellationToken cancellationToken)
    {
        var seeds = new List<(string, YamlStream)>();
        var seedDataPaths = BlueprintSeedData.ResolvePaths(blueprintMeta);
        if (seedDataPaths.Count == 0)
        {
            return seeds;
        }

        if (!string.IsNullOrEmpty(blueprintMeta.SeedDataPath) && blueprintMeta.SeedDataPaths is { Count: > 0 })
        {
            result.AddMessage(MessageCodes.BlueprintSeedDataPathAndPathsDeclared(BlueprintMetaFileName));
        }

        // CK type + rtId -> the file that declared it first.
        var rtIdOrigins = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var seedDataPath in seedDataPaths)
        {
            var seedContent = await source.ReadTextAsync(seedDataPath, cancellationToken).ConfigureAwait(false);
            if (seedContent == null)
            {
                // The single-file form has always been a warning; with several files a missing one would install a
                // partially seeded tenant.
                result.AddMessage(seedDataPaths.Count == 1
                    ? MessageCodes.BlueprintSeedDataFileMissing(BlueprintMetaFileName, seedDataPath)
                    : MessageCodes.BlueprintSeedDataFileOfSeveralMissing(BlueprintMetaFileName, seedDataPath));
                continue;
            }

            var yaml = new YamlStream();
            try
            {
                using var reader = new StringReader(seedContent);
                yaml.Load(reader);
            }
            catch (YamlDotNet.Core.YamlException ex)
            {
                result.AddMessage(MessageCodes.BlueprintSeedDataFileUnparsable(seedDataPath, ex.Message));
                continue;
            }

            seeds.Add((seedDataPath, yaml));
            CollectSeedRtIds(yaml, seedDataPath, rtIdOrigins, result);
        }

        var listed = source.ListFiles(SeedDataDirectory);
        if (listed != null)
        {
            var referenced = new HashSet<string>(seedDataPaths, StringComparer.OrdinalIgnoreCase);
            foreach (var file in listed)
            {
                var extension = Path.GetExtension(file);
                if (!string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!referenced.Contains(file))
                {
                    result.AddMessage(MessageCodes.BlueprintSeedDataFileUnreferenced(BlueprintMetaFileName, file));
                }
            }
        }

        return seeds;
    }

    /// <summary>
    ///     Identity is the CK type plus the rtId: entities live in a collection per CK type, so the same id under two
    ///     types is legal (a warning), the same identity in two files is an error, twice in one file a warning.
    /// </summary>
    private static void CollectSeedRtIds(YamlStream yaml, string seedDataPath,
        IDictionary<string, string> rtIdOrigins, OperationResult result)
    {
        if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root
            || !root.Children.TryGetValue(new YamlScalarNode("entities"), out var entitiesNode)
            || entitiesNode is not YamlSequenceNode entities)
        {
            return;
        }

        foreach (var entity in entities.Children.OfType<YamlMappingNode>())
        {
            if (!entity.Children.TryGetValue(new YamlScalarNode("rtId"), out var rtIdNode)
                || rtIdNode is not YamlScalarNode { Value: { Length: > 0 } rtId })
            {
                continue;
            }

            var ckTypeId = entity.Children.TryGetValue(new YamlScalarNode("ckTypeId"), out var ckTypeNode)
                           && ckTypeNode is YamlScalarNode { Value: { Length: > 0 } ckType }
                ? ckType
                : string.Empty;
            var identity = $"{ckTypeId}@{rtId}";

            if (!rtIdOrigins.TryGetValue(identity, out var declaredIn))
            {
                rtIdOrigins[identity] = seedDataPath;
                var reusedUnder = rtIdOrigins.Keys
                    .FirstOrDefault(k => k.EndsWith($"@{rtId}", StringComparison.Ordinal)
                                         && !string.Equals(k, identity, StringComparison.Ordinal));
                if (reusedUnder != null)
                {
                    result.AddMessage(MessageCodes.BlueprintSeedRtIdReusedAcrossTypes(seedDataPath, rtId, identity,
                        reusedUnder));
                }

                continue;
            }

            result.AddMessage(string.Equals(declaredIn, seedDataPath, StringComparison.Ordinal)
                ? MessageCodes.BlueprintSeedEntityDuplicateInFile(seedDataPath, identity)
                : MessageCodes.BlueprintSeedEntityDuplicateAcrossFiles(seedDataPath, identity, declaredIn));
        }
    }

    private async Task LintSecretSeedValuesAsync(BlueprintMetaRootDto blueprintMeta,
        IReadOnlyList<(string SeedDataPath, YamlStream Yaml)> seeds, OperationResult result,
        CancellationToken cancellationToken)
    {
        if (_secretAttributeResolver == null || seeds.Count == 0)
        {
            return;
        }

        var dependencies = new List<CkModelIdVersionRange>(blueprintMeta.CkModelDependencies ?? []);
        foreach (var seed in seeds)
        {
            dependencies.AddRange(BlueprintSeedSecretLint.ReadDependencies(seed.Yaml));
        }

        var resolution = await _secretAttributeResolver.ResolveAsync(dependencies, cancellationToken)
            .ConfigureAwait(false);
        foreach (var unresolved in resolution.UnresolvedModels.Distinct(StringComparer.Ordinal))
        {
            result.AddMessage(MessageCodes.BlueprintSecretLintModelUnresolved(BlueprintMetaFileName, unresolved));
        }

        foreach (var seed in seeds)
        {
            BlueprintSeedSecretLint.Lint(seed.Yaml, seed.SeedDataPath, resolution.SecretAttributeIds, result);
        }
    }

    private async Task ValidateCkModelDependenciesAsync(BlueprintMetaRootDto blueprintMeta,
        BlueprintValidationOptions options, OperationResult result, CancellationToken cancellationToken)
    {
        var dependencies = blueprintMeta.CkModelDependencies ?? [];
        var checkCatalogs = options.CkModelCatalogs != null && _catalogManager != null;
        var ignored = new HashSet<string>(options.IgnoredCkModels, StringComparer.Ordinal);

        if (dependencies.Count > 0 && !checkCatalogs)
        {
            result.AddMessage(MessageCodes.BlueprintCkModelDependenciesNotChecked(BlueprintMetaFileName,
                options.CkModelCatalogs == null
                    ? "no CK model catalogs were given"
                    : "no CK model catalog manager is available"));
        }

        foreach (var dependency in dependencies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (dependency == null || dependency.IsEmpty)
            {
                result.AddMessage(MessageCodes.BlueprintCkModelDependencyNameMissing(BlueprintMetaFileName));
                continue;
            }

            if (dependency.Name.StartsWith("$", StringComparison.Ordinal))
            {
                continue;
            }

            if (dependency.ModelVersionRange.MinVersion == null)
            {
                result.AddMessage(MessageCodes.BlueprintCkModelDependencyNoMinimumVersion(BlueprintMetaFileName,
                    dependency.Name, dependency.ModelVersionRange.ToString() ?? string.Empty));
                continue;
            }

            if (!checkCatalogs || ignored.Contains(dependency.Name))
            {
                continue;
            }

            await CheckDependencyInCatalogsAsync(dependency, options.CkModelCatalogs!, result).ConfigureAwait(false);
        }
    }

    private async Task CheckDependencyInCatalogsAsync(CkModelIdVersionRange dependency,
        IReadOnlyList<string> catalogNames, OperationResult result)
    {
        var range = dependency.ModelVersionRange.ToString() ?? string.Empty;
        var unreachable = false;
        try
        {
            if (catalogNames.Count == 0)
            {
                var existing = await _catalogManager!.IsExistingAsync(dependency).ConfigureAwait(false);
                if (existing.Exists)
                {
                    return;
                }

                unreachable = existing.SourceUnreachable;
            }
            else
            {
                foreach (var catalogName in catalogNames)
                {
                    var existing = await _catalogManager!.IsExistingAsync(catalogName, dependency).ConfigureAwait(false);
                    if (existing.Exists)
                    {
                        return;
                    }

                    unreachable |= existing.SourceUnreachable;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Checking CK model dependency {Dependency} failed", dependency.FullName);
            result.AddMessage(MessageCodes.BlueprintCkModelDependencyNotCheckable(BlueprintMetaFileName,
                dependency.Name, range, ex.Message));
            return;
        }

        if (unreachable)
        {
            result.AddMessage(MessageCodes.BlueprintCkModelDependencyNotCheckable(BlueprintMetaFileName,
                dependency.Name, range, "a CK model catalog could not reach its source"));
            return;
        }

        result.AddMessage(MessageCodes.BlueprintCkModelDependencyNotInCatalogs(BlueprintMetaFileName, dependency.Name,
            range, catalogNames.Count == 0 ? "all" : string.Join(", ", catalogNames)));
    }
}
