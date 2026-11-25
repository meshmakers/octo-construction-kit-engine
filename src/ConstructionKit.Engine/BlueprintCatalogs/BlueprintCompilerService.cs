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
    private readonly IBlueprintDraftValidator _draftValidator;

    /// <summary>
    /// Creates a new instance of <see cref="BlueprintCompilerService"/>.
    /// </summary>
    /// <param name="blueprintSerializer">Blueprint serializer</param>
    /// <param name="logger">Logger</param>
    /// <param name="secretAttributeResolver">
    /// Resolves the Secret attributes of the blueprint's CK models for the seed lint (AB#5528,
    /// decision 9). Without it the lint is skipped.
    /// </param>
    /// <param name="draftValidator">
    /// The shared blueprint draft validator (AB#9371); without it one without CK catalog check is created.
    /// </param>
    public BlueprintCompilerService(
        IBlueprintSerializer blueprintSerializer,
        ILogger<BlueprintCompilerService> logger,
        IBlueprintSecretAttributeResolver? secretAttributeResolver = null,
        IBlueprintDraftValidator? draftValidator = null)
    {
        _blueprintSerializer = blueprintSerializer;
        _logger = logger;
        _draftValidator = draftValidator ?? new BlueprintDraftValidator(blueprintSerializer, secretAttributeResolver);
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
    public Task<BlueprintMetaRootDto> ValidateAsync(string path, OperationResult operationResult,
        CancellationToken cancellationToken = default)
    {
        return ValidateAsync(path, null, operationResult, cancellationToken);
    }

    /// <summary>
    ///     Validates the blueprint directory <paramref name="path" /> with the shared
    ///     <see cref="IBlueprintDraftValidator" /> (AB#9371) and copies its findings into
    ///     <paramref name="operationResult" />.
    /// </summary>
    /// <param name="path">The blueprint directory</param>
    /// <param name="options">Validation options; <c>null</c> = no CK catalog check</param>
    /// <param name="operationResult">Receives the findings</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The manifest</returns>
    /// <exception cref="BlueprintCatalogException">The draft has errors</exception>
    public async Task<BlueprintMetaRootDto> ValidateAsync(string path, BlueprintValidationOptions? options,
        OperationResult operationResult, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Validating blueprint at: {Path}", path);

        var validation = await _draftValidator
            .ValidateAsync(new DirectoryBlueprintFileSource(path), options, cancellationToken)
            .ConfigureAwait(false);
        validation.CopyTo(operationResult);

        if (validation.BlueprintMeta == null)
        {
            throw new BlueprintCatalogException(
                $"Blueprint metadata file not found or unreadable: {Path.Combine(path, BlueprintMetaFileName)}");
        }

        if (!validation.IsValid)
        {
            throw new BlueprintCatalogException($"Blueprint validation failed with {validation.Findings.Count(m => m.MessageLevel == MessageLevel.Error)} error(s)");
        }

        _logger.LogInformation("Blueprint validated successfully: {BlueprintId}", validation.BlueprintMeta.BlueprintId);
        return validation.BlueprintMeta;
    }

    /// <inheritdoc />
    public Task<string> PackAsync(string path, string outputPath, OperationResult operationResult,
        CancellationToken cancellationToken = default)
    {
        return PackAsync(path, outputPath, null, operationResult, cancellationToken);
    }

    /// <summary>
    ///     Validates (see <see cref="ValidateAsync(string, BlueprintValidationOptions?, OperationResult, CancellationToken)" />)
    ///     and packs the blueprint directory into <c>&lt;outputPath&gt;/&lt;Name-Version&gt;.zip</c>.
    /// </summary>
    public async Task<string> PackAsync(string path, string outputPath, BlueprintValidationOptions? options,
        OperationResult operationResult, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Packing blueprint at: {Path}", path);

        // Validate first
        var blueprintMeta = await ValidateAsync(path, options, operationResult, cancellationToken).ConfigureAwait(false);

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
