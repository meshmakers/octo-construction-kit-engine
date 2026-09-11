using System.IO.Compression;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Microsoft.Extensions.Logging;
using YamlDotNet.RepresentationModel;

namespace Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;

/// <summary>
/// Service for blueprint compilation and management operations.
/// </summary>
public class BlueprintCompilerService : IBlueprintCompilerService
{
    private const string BlueprintMetaFileName = "blueprint.yaml";
    private const string SeedDataDirectory = "seed-data";

    private readonly IBlueprintSerializer _blueprintSerializer;
    private readonly ILogger<BlueprintCompilerService> _logger;

    /// <summary>
    /// Creates a new instance of <see cref="BlueprintCompilerService"/>.
    /// </summary>
    public BlueprintCompilerService(
        IBlueprintSerializer blueprintSerializer,
        ILogger<BlueprintCompilerService> logger)
    {
        _blueprintSerializer = blueprintSerializer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task CreateNewAsync(string path, string blueprintName, string version = "1.0.0",
        CancellationToken cancellationToken = default)
    {
        var blueprintId = new BlueprintId(blueprintName, version);
        var blueprintDir = Path.Combine(path, blueprintId.FullName);

        _logger.LogInformation("Creating blueprint directory: {Path}", blueprintDir);

        if (Directory.Exists(blueprintDir))
        {
            throw new BlueprintCatalogException($"Blueprint directory already exists: {blueprintDir}");
        }

        // Create directories
        Directory.CreateDirectory(blueprintDir);
        Directory.CreateDirectory(Path.Combine(blueprintDir, SeedDataDirectory));

        // Create blueprint.yaml
        var blueprintMeta = new BlueprintMetaRootDto
        {
            BlueprintId = blueprintId,
            Description = $"{blueprintName} blueprint",
            CkModelDependencies = new List<CkModelIdVersionRange>
            {
                new("System", "[2.0,)")
            },
            SeedDataPath = $"{SeedDataDirectory}/entities.yaml"
        };

        var blueprintMetaPath = Path.Combine(blueprintDir, BlueprintMetaFileName);
#if NETSTANDARD2_0
        using var writer = new StreamWriter(blueprintMetaPath);
#else
        await using var writer = new StreamWriter(blueprintMetaPath);
#endif
        await _blueprintSerializer.SerializeAsync(writer, blueprintMeta).ConfigureAwait(false);

        // Create empty seed data file
        var seedDataPath = Path.Combine(blueprintDir, SeedDataDirectory, "entities.yaml");
        var seedDataContent = $"""
            # Seed data for {blueprintName}
            # Add your initial entities here
            $schema: https://schemas.meshmakers.cloud/runtime-model.schema.json
            dependencies:
              - System-2.0.0
            entities: []
            """;
#if NETSTANDARD2_0
        using (var seedWriter = new StreamWriter(seedDataPath))
        {
            await seedWriter.WriteAsync(seedDataContent).ConfigureAwait(false);
        }
#else
        await File.WriteAllTextAsync(seedDataPath, seedDataContent, cancellationToken).ConfigureAwait(false);
#endif

        _logger.LogInformation("Blueprint created successfully: {BlueprintId}", blueprintId);
    }

    /// <inheritdoc />
    public async Task<BlueprintMetaRootDto> ValidateAsync(string path, OperationResult operationResult,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Validating blueprint at: {Path}", path);

        var blueprintMetaPath = Path.Combine(path, BlueprintMetaFileName);
        if (!File.Exists(blueprintMetaPath))
        {
            operationResult.AddMessage(new OperationMessage(
                MessageLevel.Error,
                path,
                1,
                $"Blueprint metadata file not found: {BlueprintMetaFileName}"));
            throw new BlueprintCatalogException($"Blueprint metadata file not found: {blueprintMetaPath}");
        }

        string content;
#if NETSTANDARD2_0
        using (var reader = new StreamReader(blueprintMetaPath))
        {
            content = await reader.ReadToEndAsync().ConfigureAwait(false);
        }
#else
        content = await File.ReadAllTextAsync(blueprintMetaPath, cancellationToken).ConfigureAwait(false);
#endif
        var blueprintMeta = _blueprintSerializer.DeserializeBlueprintMeta(content, blueprintMetaPath, operationResult);

        // Validate blueprint ID
        if (blueprintMeta.BlueprintId.IsEmpty)
        {
            operationResult.AddMessage(new OperationMessage(
                MessageLevel.Error,
                blueprintMetaPath,
                2,
                "Blueprint ID is required"));
        }

        // Validate the declared seed data file(s). A blueprint may split its seed across several
        // files and folders (AB#4758); every declared file must exist and no rtId may be declared
        // twice across them.
        ValidateSeedData(path, blueprintMeta, blueprintMetaPath, operationResult);

        // Validate CK model dependencies format
        if (blueprintMeta.CkModelDependencies != null)
        {
            foreach (var dep in blueprintMeta.CkModelDependencies)
            {
                if (string.IsNullOrWhiteSpace(dep.Name))
                {
                    operationResult.AddMessage(new OperationMessage(
                        MessageLevel.Error,
                        blueprintMetaPath,
                        4,
                        "CK model dependency name is required"));
                }
            }
        }

        if (operationResult.HasErrors)
        {
            throw new BlueprintCatalogException($"Blueprint validation failed with {operationResult.Messages.Count(m => m.MessageLevel == MessageLevel.Error)} error(s)");
        }

        _logger.LogInformation("Blueprint validated successfully: {BlueprintId}", blueprintMeta.BlueprintId);
        return blueprintMeta;
    }


    /// <summary>
    /// Validates the seed-data files a blueprint declares: each file exists, no <c>rtId</c> is
    /// declared in two different files, and no stray YAML file sits unreferenced in
    /// <c>seed-data/</c>.
    /// </summary>
    private static void ValidateSeedData(string blueprintPath, BlueprintMetaRootDto blueprintMeta,
        string blueprintMetaPath, OperationResult operationResult)
    {
        var seedDataPaths = BlueprintSeedData.ResolvePaths(blueprintMeta);
        if (seedDataPaths.Count == 0)
        {
            return;
        }

        if (!string.IsNullOrEmpty(blueprintMeta.SeedDataPath) && blueprintMeta.SeedDataPaths is { Count: > 0 })
        {
            operationResult.AddMessage(new OperationMessage(
                MessageLevel.Warning,
                blueprintMetaPath,
                5,
                "Blueprint declares both 'seedDataPath' and 'seedDataPaths'. Both are loaded "
                + "('seedDataPath' first), but listing every file in 'seedDataPaths' keeps the seed in one place."));
        }

        // rtId -> the file that declared it first.
        var rtIdOrigins = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var seedDataPath in seedDataPaths)
        {
            var seedDataFullPath = Path.Combine(blueprintPath,
                seedDataPath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(seedDataFullPath))
            {
                // The single-file form has always reported this as a warning. With several declared
                // files a missing one would install a partially seeded tenant, so it is an error.
                operationResult.AddMessage(new OperationMessage(
                    seedDataPaths.Count == 1 ? MessageLevel.Warning : MessageLevel.Error,
                    blueprintMetaPath,
                    3,
                    $"Seed data file not found: {seedDataPath}"));
                continue;
            }

            CollectSeedRtIds(seedDataFullPath, seedDataPath, rtIdOrigins, operationResult);
        }

        WarnAboutUnreferencedSeedFiles(blueprintPath, seedDataPaths, blueprintMetaPath, operationResult);
    }

    /// <summary>
    /// Records the identity of every entity in one seed-data file and which file declared it first.
    /// A repeat across two files is an error (which entity wins would depend on the file order); a
    /// repeat inside one file is a warning, matching what the runtime importer does.
    /// </summary>
    /// <remarks>
    /// Identity is the CK type plus the <c>rtId</c>, not the <c>rtId</c> alone: entities live in a
    /// collection per CK type and associations carry the target type next to the target id, so the
    /// same id under two types is legal and does occur in shipped blueprints. It is still worth a
    /// warning — one careless copy of an association target then points at the wrong entity.
    /// Deliberately parsed with the YAML representation model rather than the runtime-model DTOs:
    /// those live in Runtime.Contracts, which the CK engine does not reference, and the check only
    /// needs two scalars per entity.
    /// </remarks>
    private static void CollectSeedRtIds(string seedDataFullPath, string seedDataPath,
        IDictionary<string, string> rtIdOrigins, OperationResult operationResult)
    {
        var yaml = new YamlStream();
        try
        {
            using var reader = new StreamReader(seedDataFullPath);
            yaml.Load(reader);
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            operationResult.AddMessage(new OperationMessage(
                MessageLevel.Error, seedDataPath, 6, $"Seed data file could not be parsed: {ex.Message}"));
            return;
        }

        if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
        {
            return;
        }

        if (!root.Children.TryGetValue(new YamlScalarNode("entities"), out var entitiesNode)
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

                // Same id under a different type: legal, but a copied association target would now
                // silently point at the other entity.
                var reusedUnder = rtIdOrigins.Keys
                    .FirstOrDefault(k => k.EndsWith($"@{rtId}", StringComparison.Ordinal)
                                         && !string.Equals(k, identity, StringComparison.Ordinal));
                if (reusedUnder != null)
                {
                    operationResult.AddMessage(new OperationMessage(
                        MessageLevel.Warning,
                        seedDataPath,
                        9,
                        $"rtId '{rtId}' is used by '{identity}' and by '{reusedUnder}'. That is legal "
                        + "(entities are keyed by CK type plus rtId) but makes association targets easy "
                        + "to get wrong - prefer a distinct id per entity."));
                }

                continue;
            }

            var sameFile = string.Equals(declaredIn, seedDataPath, StringComparison.Ordinal);
            operationResult.AddMessage(new OperationMessage(
                sameFile ? MessageLevel.Warning : MessageLevel.Error,
                seedDataPath,
                7,
                sameFile
                    ? $"Duplicate entity '{identity}' in the same seed data file"
                    : $"Duplicate entity '{identity}' — already declared in '{declaredIn}'"));
        }
    }

    /// <summary>
    /// Warns about YAML files under <c>seed-data/</c> that no declared seed path names. Forgetting
    /// to add a newly created file to <c>seedDataPaths</c> is the one failure mode the split form
    /// introduces, and it would otherwise pass silently as a smaller-than-expected install.
    /// </summary>
    private static void WarnAboutUnreferencedSeedFiles(string blueprintPath,
        IReadOnlyList<string> seedDataPaths, string blueprintMetaPath, OperationResult operationResult)
    {
        var seedDataDirectory = Path.Combine(blueprintPath, SeedDataDirectory);
        if (!Directory.Exists(seedDataDirectory))
        {
            return;
        }

        var referenced = new HashSet<string>(seedDataPaths, StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.GetFiles(seedDataDirectory, "*.*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(file);
            if (!string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = BlueprintSeedData.Normalise(GetRelativePath(blueprintPath, file));
            if (referenced.Contains(relativePath))
            {
                continue;
            }

            operationResult.AddMessage(new OperationMessage(
                MessageLevel.Warning,
                blueprintMetaPath,
                8,
                $"Seed data file '{relativePath}' is not referenced by 'seedDataPath' / 'seedDataPaths' "
                + "and will not be imported"));
        }
    }

    private static string GetRelativePath(string relativeTo, string path)
    {
#if NETSTANDARD2_0
        var relativeToUri = new Uri(relativeTo.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
        var pathUri = new Uri(path);
        var relativeUri = relativeToUri.MakeRelativeUri(pathUri);
        return Uri.UnescapeDataString(relativeUri.ToString().Replace('/', Path.DirectorySeparatorChar));
#else
        return Path.GetRelativePath(relativeTo, path);
#endif
    }

    /// <inheritdoc />
    public async Task<string> PackAsync(string path, string outputPath, OperationResult operationResult,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Packing blueprint at: {Path}", path);

        // Validate first
        var blueprintMeta = await ValidateAsync(path, operationResult, cancellationToken).ConfigureAwait(false);

        // Create output directory if needed
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }

        // Create zip file
        var zipFileName = $"{blueprintMeta.BlueprintId.FullName}.zip";
        var zipFilePath = Path.Combine(outputPath, zipFileName);

        if (File.Exists(zipFilePath))
        {
            File.Delete(zipFilePath);
        }

        ZipFile.CreateFromDirectory(path, zipFilePath, CompressionLevel.Optimal, true);

        _logger.LogInformation("Blueprint packed successfully: {ZipPath}", zipFilePath);
        return zipFilePath;
    }
}
