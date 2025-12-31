using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
/// Parses blueprint migration scripts from YAML files
/// </summary>
public interface IMigrationParser
{
    /// <summary>
    /// Parses a migration script from a file path
    /// </summary>
    /// <param name="filePath">Path to the migration YAML file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Parsed migration DTO</returns>
    Task<BlueprintMigrationDto> ParseAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses a migration script from a string
    /// </summary>
    /// <param name="yaml">YAML content</param>
    /// <returns>Parsed migration DTO</returns>
    BlueprintMigrationDto Parse(string yaml);
}

/// <summary>
/// Parses blueprint migration scripts from YAML files
/// </summary>
internal class MigrationParser : IMigrationParser
{
    private readonly IDeserializer _deserializer;

    /// <summary>
    /// Creates a new instance of <see cref="MigrationParser"/>
    /// </summary>
    public MigrationParser()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <inheritdoc />
    public async Task<BlueprintMigrationDto> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Migration file not found: {filePath}", filePath);
        }

#if NETSTANDARD2_0
        using var reader = new StreamReader(filePath);
        var yaml = await reader.ReadToEndAsync().ConfigureAwait(false);
#else
        var yaml = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
#endif

        return Parse(yaml);
    }

    /// <inheritdoc />
    public BlueprintMigrationDto Parse(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            throw new ArgumentException("YAML content cannot be empty", nameof(yaml));
        }

        try
        {
            var migration = _deserializer.Deserialize<BlueprintMigrationDto>(yaml);

            if (migration == null)
            {
                throw new InvalidOperationException("Failed to parse migration YAML - result was null");
            }

            // Validate required fields
            if (string.IsNullOrEmpty(migration.SourceVersion))
            {
                throw new InvalidOperationException("Migration sourceVersion is required");
            }

            if (string.IsNullOrEmpty(migration.TargetVersion))
            {
                throw new InvalidOperationException("Migration targetVersion is required");
            }

            return migration;
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new InvalidOperationException($"Failed to parse migration YAML: {ex.Message}", ex);
        }
    }
}
