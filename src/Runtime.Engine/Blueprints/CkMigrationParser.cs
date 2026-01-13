using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
/// Parses CK model migration scripts from YAML files
/// </summary>
public interface ICkMigrationParser
{
    /// <summary>
    /// Parses a CK migration meta file from a file path
    /// </summary>
    /// <param name="filePath">Path to the migration-meta.yaml file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Parsed migration meta DTO</returns>
    Task<CkMigrationMetaDto> ParseMetaAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses a CK migration script from a file path
    /// </summary>
    /// <param name="filePath">Path to the migration script YAML file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Parsed migration script DTO</returns>
    Task<CkMigrationScriptDto> ParseScriptAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses a CK migration meta from a stream
    /// </summary>
    /// <param name="stream">Stream containing YAML content</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Parsed migration meta DTO</returns>
    Task<CkMigrationMetaDto> ParseMetaFromStreamAsync(Stream stream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses a CK migration script from a stream
    /// </summary>
    /// <param name="stream">Stream containing YAML content</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Parsed migration script DTO</returns>
    Task<CkMigrationScriptDto> ParseScriptFromStreamAsync(Stream stream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses a CK migration meta from a string
    /// </summary>
    /// <param name="yaml">YAML content</param>
    /// <returns>Parsed migration meta DTO</returns>
    CkMigrationMetaDto ParseMeta(string yaml);

    /// <summary>
    /// Parses a CK migration script from a string
    /// </summary>
    /// <param name="yaml">YAML content</param>
    /// <returns>Parsed migration script DTO</returns>
    CkMigrationScriptDto ParseScript(string yaml);
}

/// <summary>
/// Parses CK model migration scripts from YAML files
/// </summary>
internal class CkMigrationParser : ICkMigrationParser
{
    private readonly IDeserializer _deserializer;

    /// <summary>
    /// Creates a new instance of <see cref="CkMigrationParser"/>
    /// </summary>
    public CkMigrationParser()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <inheritdoc />
    public async Task<CkMigrationMetaDto> ParseMetaAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"CK migration meta file not found: {filePath}", filePath);
        }

#if NETSTANDARD2_0
        using var reader = new StreamReader(filePath);
        var yaml = await reader.ReadToEndAsync().ConfigureAwait(false);
#else
        var yaml = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
#endif

        return ParseMeta(yaml);
    }

    /// <inheritdoc />
    public async Task<CkMigrationScriptDto> ParseScriptAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"CK migration script file not found: {filePath}", filePath);
        }

#if NETSTANDARD2_0
        using var reader = new StreamReader(filePath);
        var yaml = await reader.ReadToEndAsync().ConfigureAwait(false);
#else
        var yaml = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
#endif

        return ParseScript(yaml);
    }

    /// <inheritdoc />
    public async Task<CkMigrationMetaDto> ParseMetaFromStreamAsync(Stream stream, CancellationToken cancellationToken = default)
    {
#if NETSTANDARD2_0
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true, 4096, leaveOpen: true);
        var yaml = await reader.ReadToEndAsync().ConfigureAwait(false);
#else
        using var reader = new StreamReader(stream, leaveOpen: true);
        var yaml = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
#endif
        return ParseMeta(yaml);
    }

    /// <inheritdoc />
    public async Task<CkMigrationScriptDto> ParseScriptFromStreamAsync(Stream stream, CancellationToken cancellationToken = default)
    {
#if NETSTANDARD2_0
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true, 4096, leaveOpen: true);
        var yaml = await reader.ReadToEndAsync().ConfigureAwait(false);
#else
        using var reader = new StreamReader(stream, leaveOpen: true);
        var yaml = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
#endif
        return ParseScript(yaml);
    }

    /// <inheritdoc />
    public CkMigrationMetaDto ParseMeta(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            throw new ArgumentException("YAML content cannot be empty", nameof(yaml));
        }

        try
        {
            var meta = _deserializer.Deserialize<CkMigrationMetaDto>(yaml);

            if (meta == null)
            {
                throw new InvalidOperationException("Failed to parse CK migration meta YAML - result was null");
            }

            if (string.IsNullOrEmpty(meta.CkModelId))
            {
                throw new InvalidOperationException("CK migration meta ckModelId is required");
            }

            return meta;
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new InvalidOperationException($"Failed to parse CK migration meta YAML: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public CkMigrationScriptDto ParseScript(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            throw new ArgumentException("YAML content cannot be empty", nameof(yaml));
        }

        try
        {
            var script = _deserializer.Deserialize<CkMigrationScriptDto>(yaml);

            if (script == null)
            {
                throw new InvalidOperationException("Failed to parse CK migration script YAML - result was null");
            }

            if (string.IsNullOrEmpty(script.SourceVersion))
            {
                throw new InvalidOperationException("CK migration script sourceVersion is required");
            }

            if (string.IsNullOrEmpty(script.TargetVersion))
            {
                throw new InvalidOperationException("CK migration script targetVersion is required");
            }

            return script;
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new InvalidOperationException($"Failed to parse CK migration script YAML: {ex.Message}", ex);
        }
    }
}
