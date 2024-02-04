using System.Text;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Meshmakers.Octo.ConstructionKit.Engine.Serialization;

/// <summary>
///     Implements a serializer for the CK model in YAML format.
/// </summary>
/// <remarks>
///     Currently there is no YAML serializer that supports JSON schema validation
///     out of the box. Therefore we use the YamlDotNet library and implement the validation
///     using the <see cref="ICkSchemaValidator" /> interface. That results that the stream
///     is used twice: for validation and for deserialization. This is not optimal.
/// </remarks>
internal class CkYamlSerializer : ICkYamlSerializer
{
    private readonly ICkSchemaValidator _ckSchemaValidator;
    private readonly IDeserializer _deserializer;
    private readonly ISerializer _serializer;

    // ReSharper disable once ConvertConstructorToMemberInitializers
    /// <summary>
    ///     Creates a new instance of the <see cref="CkYamlSerializer" /> class.
    /// </summary>
    /// <param name="ckSchemaValidator"></param>
    public CkYamlSerializer(ICkSchemaValidator ckSchemaValidator)
    {
        _ckSchemaValidator = ckSchemaValidator;

        _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new CkModelIdConverter())
            .WithTypeConverter(new CkTypeIdConverter())
            .WithTypeConverter(new CkRecordIdConverter())
            .WithTypeConverter(new CkEnumIdConverter())
            .WithTypeConverter(new CkAttributeIdConverter())
            .WithTypeConverter(new CkAssociationRoleIdConverter())
            .WithTypeConverter(new CkIdAttributeIdConverter())
            .WithTypeConverter(new CkIdTypeIdConverter())
            .WithTypeConverter(new CkIdRecordIdConverter())
            .WithTypeConverter(new CkIdEnumIdConverter())
            .WithTypeConverter(new CkIdAssociationRoleIdConverter())
            .Build();
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new CkModelIdConverter())
            .WithTypeConverter(new CkTypeIdConverter())
            .WithTypeConverter(new CkRecordIdConverter())
            .WithTypeConverter(new CkEnumIdConverter())
            .WithTypeConverter(new CkAttributeIdConverter())
            .WithTypeConverter(new CkAssociationRoleIdConverter())
            .WithTypeConverter(new CkIdAttributeIdConverter())
            .WithTypeConverter(new CkIdTypeIdConverter())
            .WithTypeConverter(new CkIdRecordIdConverter())
            .WithTypeConverter(new CkIdEnumIdConverter())
            .WithTypeConverter(new CkIdAssociationRoleIdConverter())
         //   .WithTypeConverter(new ObjectCollectionConverter())
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
    public Task<CkMetaRootDto> DeserializeMetaAsync(Stream stream, string locationReference, OperationResult operationResult)
    {
        _ckSchemaValidator.ValidateMetaInYaml(stream, locationReference, operationResult);
        if (operationResult.HasErrors)
        {
            throw ModelParseException.SchemaValidationFailed(locationReference, operationResult);
        }

        using var streamReader = new StreamReader(stream);
        var ckMetaDto = _deserializer.Deserialize<CkMetaRootDto>(streamReader);
        return Task.FromResult(ckMetaDto ?? throw ModelParseException.CannotDeserializeModel(operationResult));
    }

    /// <inheritdoc />
    public Task<CkElementsRootDto> DeserializeElementsAsync(Stream stream, string locationReference, OperationResult operationResult)
    {
        _ckSchemaValidator.ValidateElementsInYaml(stream, locationReference, operationResult);
        if (operationResult.HasErrors)
        {
            throw ModelParseException.SchemaValidationFailed(locationReference, operationResult);
        }

        using var streamReader = new StreamReader(stream);
        var ckElementsDto = _deserializer.Deserialize<CkElementsRootDto>(streamReader);
        return Task.FromResult(ckElementsDto ?? throw ModelParseException.CannotDeserializeModel(operationResult));
    }

    /// <inheritdoc />
    public async Task<CkCompiledModelRoot> DeserializeCompiledModelRootAsync(string s, string locationReference,
        OperationResult operationResult)
    {
        var byteArray = Encoding.UTF8.GetBytes(s);
        using var memStream = new MemoryStream(byteArray);
        var ckCompiledModelRoot = await DeserializeCompiledModelRootAsync(memStream, locationReference, operationResult).ConfigureAwait(false);
        return ckCompiledModelRoot ?? throw ModelParseException.CannotDeserializeModel(operationResult);
    }

    /// <inheritdoc />
    public CkCompiledModelRoot DeserializeCompiledModelRoot(string s, string locationReference, OperationResult operationResult)
    {
        var byteArray = Encoding.UTF8.GetBytes(s);
        using var memStream = new MemoryStream(byteArray);
        var ckCompiledModelRoot =  DeserializeCompiledModelRoot(memStream, locationReference, operationResult);
        return ckCompiledModelRoot ?? throw ModelParseException.CannotDeserializeModel(operationResult);
    }

    /// <inheritdoc />
    public Task<CkCompiledModelRoot> DeserializeCompiledModelRootAsync(Stream stream, string locationReference,
        OperationResult operationResult)
    {
        return Task.FromResult(DeserializeCompiledModelRoot(stream, locationReference, operationResult));
    }

    private CkCompiledModelRoot DeserializeCompiledModelRoot(Stream stream, string locationReference, OperationResult operationResult)
    {
        _ckSchemaValidator.ValidateCompiledModelInYaml(stream, locationReference, operationResult);
        if (operationResult.HasErrors)
        {
            throw ModelParseException.SchemaValidationFailed(locationReference, operationResult);
        }

        using var streamReader = new StreamReader(stream);
        var ckModelRoot = _deserializer.Deserialize<CkCompiledModelRoot>(streamReader);
        return ckModelRoot ?? throw ModelParseException.CannotDeserializeModel(operationResult);
    }
}