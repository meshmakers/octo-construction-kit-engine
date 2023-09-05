using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Meshmakers.Octo.ConstructionKit.Engine.Serialization;

/// <summary>
/// Implements a serializer for the CK model in YAML format.
/// </summary>
/// <remarks>
/// Currently there is no YAML serializer that supports JSON schema validation
/// out of the box. Therefore we use the YamlDotNet library and implement the validation
/// using the <see cref="ICkSchemaValidator"/> interface. That results that the stream
/// is used twice: for validation and for deserialization. This is not optimal.
/// </remarks>
public class CkYamlSerializer : ICkYamlSerializer
{
    private readonly ICkSchemaValidator _ckSchemaValidator;
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    // ReSharper disable once ConvertConstructorToMemberInitializers
    /// <summary>
    /// Creates a new instance of the <see cref="CkYamlSerializer"/> class.
    /// </summary>
    /// <param name="ckSchemaValidator"></param>
    public CkYamlSerializer(ICkSchemaValidator ckSchemaValidator)
    {
        _ckSchemaValidator = ckSchemaValidator;

        _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new CkModelIdConverter())
            .WithTypeConverter(new CkTypeIdConverter())
            .WithTypeConverter(new CkAttributeIdConverter())
            .WithTypeConverter(new CkAssociationIdConverter())
            .WithTypeConverter(new CkIdAttributeIdConverter())
            .WithTypeConverter(new CkIdTypeIdConverter())
            .WithTypeConverter(new CkIdAssociationIdConverter())
            .Build();
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new CkModelIdConverter())
            .WithTypeConverter(new CkTypeIdConverter())
            .WithTypeConverter(new CkAttributeIdConverter())
            .WithTypeConverter(new CkAssociationIdConverter())
            .WithTypeConverter(new CkIdAttributeIdConverter())
            .WithTypeConverter(new CkIdTypeIdConverter())
            .WithTypeConverter(new CkIdAssociationIdConverter())
            .IgnoreUnmatchedProperties() // set because $schema is not in the model and we don't want to fail on it
            .Build();
    }

    /// <inheritdoc />
    public Task SerializeAsync(StreamWriter streamWriter, CkCompiledModelRoot compiledModel)
    {
        _serializer.Serialize(streamWriter, compiledModel);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SerializeAsync(StreamWriter streamWriter, CkMetaRootDto metaRootDto)
    {
        _serializer.Serialize(streamWriter, metaRootDto);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SerializeAsync(StreamWriter streamWriter, CkElementsRootDto elementsRootDto)
    {
        _serializer.Serialize(streamWriter, elementsRootDto);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<CkMetaRootDto> DeserializeMetaAsync(Stream stream, OperationResult operationResult)
    {
        _ckSchemaValidator.ValidateMetaInYaml(stream, operationResult);
        if (operationResult.HasErrors)
        {
            throw ModelParseException.SchemaValidationFailed();
        }

        using var streamReader = new StreamReader(stream);
        var ckMetaDto = _deserializer.Deserialize<CkMetaRootDto>(streamReader);
        return Task.FromResult(ckMetaDto);
    }

    /// <inheritdoc />
    public Task<CkElementsRootDto> DeserializeElementsAsync(Stream stream, OperationResult operationResult)
    {
        _ckSchemaValidator.ValidateElementsInYaml(stream, operationResult);
        if (operationResult.HasErrors)
        {
            throw ModelParseException.SchemaValidationFailed();
        }

        using var streamReader = new StreamReader(stream);
        var ckElementsDto = _deserializer.Deserialize<CkElementsRootDto>(streamReader);
        return Task.FromResult(ckElementsDto);
    }

    /// <inheritdoc />
    public async Task<CkCompiledModelRoot> DeserializeCompiledModelRootAsync(string s, OperationResult operationResult)
    {
        byte[] byteArray = System.Text.Encoding.UTF8.GetBytes(s);
        using var memStream = new MemoryStream(byteArray);
        return await DeserializeCompiledModelRootAsync(memStream, operationResult);
    }

    /// <inheritdoc />
    public CkCompiledModelRoot DeserializeCompiledModelRoot(string s, OperationResult operationResult)
    {
        byte[] byteArray = System.Text.Encoding.UTF8.GetBytes(s);
        using var memStream = new MemoryStream(byteArray);
        return DeserializeCompiledModelRoot(memStream, operationResult);
    }

    /// <inheritdoc />
    public Task<CkCompiledModelRoot> DeserializeCompiledModelRootAsync(Stream stream, OperationResult operationResult)
    {
        return Task.FromResult(DeserializeCompiledModelRoot(stream, operationResult));
    }
    
    private CkCompiledModelRoot DeserializeCompiledModelRoot(Stream stream, OperationResult operationResult)
    {
        _ckSchemaValidator.ValidateCompiledModelInYaml(stream, operationResult);
        if (operationResult.HasErrors)
        {
            throw ModelParseException.SchemaValidationFailed();
        }

        using var streamReader = new StreamReader(stream);
        var ckModelRoot = _deserializer.Deserialize<CkCompiledModelRoot>(streamReader);
        return ckModelRoot;
    }
}