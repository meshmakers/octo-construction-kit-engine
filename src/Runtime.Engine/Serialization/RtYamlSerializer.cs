using System.Text;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Meshmakers.Octo.Runtime.Engine.Serialization;

/// <summary>
///     Implements a serializer for the runtime model in YAML format.
/// </summary>
/// <remarks>
///     Currently there is no YAML serializer that supports JSON schema validation
///     out of the box. Therefore we use the YamlDotNet library and implement the validation
///     using the <see cref="IRtSchemaValidator" /> interface. That results that the stream
///     is used twice: for validation and for deserialization. This is not optimal.
/// </remarks>
internal class RtYamlSerializer : IRtYamlSerializer
{
    private readonly IDeserializer _deserializer;
    private readonly IRtSchemaValidator _rtSchemaValidator;
    private readonly ISerializer _serializer;

    // ReSharper disable once ConvertConstructorToMemberInitializers
    /// <summary>
    ///     Creates a new instance of the <see cref="RtYamlSerializer" /> class.
    /// </summary>
    /// <param name="rtSchemaValidator"></param>
    public RtYamlSerializer(IRtSchemaValidator rtSchemaValidator)
    {
        _rtSchemaValidator = rtSchemaValidator;

        _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new CkModelIdConverter())
            .WithTypeConverter(new CkModelIdVersionRangeConverter())
            .WithTypeConverter(new CkTypeIdConverter())
            .WithTypeConverter(new CkRecordIdConverter())
            .WithTypeConverter(new CkEnumIdConverter())
            .WithTypeConverter(new CkAttributeIdConverter())
            .WithTypeConverter(new CkAssociationRoleIdConverter())
            .WithTypeConverter(new RtCkIdAttributeIdConverter())
            .WithTypeConverter(new RtCkIdTypeIdConverter())
            .WithTypeConverter(new RtCkIdRecordIdConverter())
            .WithTypeConverter(new RtCkIdEnumIdConverter())
            .WithTypeConverter(new RtCkIdAssociationRoleIdConverter())
            .WithTypeConverter(new OctoObjectIdConverter())
            .WithEventEmitter(next => new MultilineScalarStyleEmitter(next))
            .Build();
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new CkModelIdConverter())
            .WithTypeConverter(new CkModelIdVersionRangeConverter())
            .WithTypeConverter(new CkTypeIdConverter())
            .WithTypeConverter(new CkRecordIdConverter())
            .WithTypeConverter(new CkEnumIdConverter())
            .WithTypeConverter(new CkAttributeIdConverter())
            .WithTypeConverter(new CkAssociationRoleIdConverter())
            .WithTypeConverter(new RtCkIdAttributeIdConverter())
            .WithTypeConverter(new RtCkIdTypeIdConverter())
            .WithTypeConverter(new RtCkIdRecordIdConverter())
            .WithTypeConverter(new RtCkIdEnumIdConverter())
            .WithTypeConverter(new RtCkIdAssociationRoleIdConverter())
            .WithTypeConverter(new OctoObjectIdConverter())
            .WithTypeConverter(new RtRecordConverter())
            .IgnoreUnmatchedProperties() // set because $schema is not in the model and we don't want to fail on it
            .Build();
    }

    /// <inheritdoc />
    public Task SerializeAsync(StreamWriter streamWriter, RtModelRootTcDto modelRootDto)
    {
        _serializer.Serialize(streamWriter, modelRootDto);
        return Task.CompletedTask;
    }

    public Task<IRtDeserializeStream> DeserializeStreamAsync(Stream stream,
        CancellationToken? cancellationToken = null)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public async Task<RtModelRootTcDto> DeserializeAsync(string s, string locationReference, OperationResult operationResult)
    {
        var byteArray = Encoding.UTF8.GetBytes(s);
        using var memStream = new MemoryStream(byteArray);
        return await DeserializeAsync(memStream, locationReference, operationResult).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<RtModelRootTcDto> DeserializeAsync(Stream stream, string locationReference, OperationResult operationResult)
    {
        _rtSchemaValidator.ValidateModelInYaml(stream, locationReference, operationResult);
        if (operationResult.HasFatalErrors)
        {
            throw RuntimeModelParseException.SchemaValidationFailed(locationReference, operationResult);
        }


        using var streamReader = new StreamReader(stream);
        var rtModelRootDto = _deserializer.Deserialize<RtModelRootTcDto>(streamReader);
        return Task.FromResult(rtModelRootDto);
    }
}