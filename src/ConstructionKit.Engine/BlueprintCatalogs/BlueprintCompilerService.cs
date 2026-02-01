using System.IO.Compression;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Microsoft.Extensions.Logging;

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
            ComposedBlueprints = new List<BlueprintIdVersionRange>(),
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

        // Validate seed data path if specified
        if (!string.IsNullOrEmpty(blueprintMeta.SeedDataPath))
        {
            var seedDataFullPath = Path.Combine(path, blueprintMeta.SeedDataPath);
            if (!File.Exists(seedDataFullPath))
            {
                operationResult.AddMessage(new OperationMessage(
                    MessageLevel.Warning,
                    blueprintMetaPath,
                    3,
                    $"Seed data file not found: {blueprintMeta.SeedDataPath}"));
            }
        }

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
