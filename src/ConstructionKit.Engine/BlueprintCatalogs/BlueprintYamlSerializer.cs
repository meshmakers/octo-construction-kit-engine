using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;

/// <summary>
/// Implements a serializer for blueprints in YAML format.
/// </summary>
internal class BlueprintYamlSerializer : IBlueprintSerializer
{
    private readonly IDeserializer _deserializer;
    private readonly ISerializer _serializer;

    /// <summary>
    /// Creates a new instance of the <see cref="BlueprintYamlSerializer" /> class.
    /// </summary>
    public BlueprintYamlSerializer()
    {
        _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new BlueprintIdConverter())
            .WithTypeConverter(new BlueprintIdVersionRangeConverter())
            .WithTypeConverter(new CkModelIdVersionRangeConverter())
            .Build();

        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new BlueprintIdConverter())
            .WithTypeConverter(new BlueprintIdVersionRangeConverter())
            .WithTypeConverter(new CkModelIdVersionRangeConverter())
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <inheritdoc />
    public Task SerializeAsync(StreamWriter streamWriter, BlueprintMetaRootDto blueprintMetaRoot)
    {
        _serializer.Serialize(streamWriter, blueprintMetaRoot);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<BlueprintMetaRootDto> DeserializeBlueprintMetaAsync(Stream stream, string locationReference, OperationResult operationResult)
    {
        using var streamReader = new StreamReader(stream);
        var content = await streamReader.ReadToEndAsync().ConfigureAwait(false);
        return DeserializeBlueprintMeta(content, locationReference, operationResult);
    }

    /// <inheritdoc />
    public BlueprintMetaRootDto DeserializeBlueprintMeta(string content, string locationReference, OperationResult operationResult)
    {
        try
        {
            var blueprintMeta = _deserializer.Deserialize<BlueprintMetaRootDto>(content);
            if (blueprintMeta == null)
            {
                operationResult.AddMessage(new OperationMessage(
                    MessageLevel.Error,
                    locationReference,
                    1,
                    $"Failed to deserialize blueprint: result was null"));
                throw new BlueprintCatalogException($"Failed to deserialize blueprint at '{locationReference}'");
            }

            return blueprintMeta;
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            operationResult.AddMessage(new OperationMessage(
                MessageLevel.Error,
                locationReference,
                2,
                $"YAML parsing error: {ex.Message}"));
            throw new BlueprintCatalogException($"Failed to parse blueprint YAML at '{locationReference}': {ex.Message}", ex);
        }
    }
}
