using System.Text.Json;
using System.Text.Json.Serialization;
using Json.Schema;
using Meshmakers.Common.Shared;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Engine.Messages;

namespace Meshmakers.Octo.Runtime.Engine.Serialization;

internal class RtRepositorySerializer : IRtRepositorySerializer
{
    private const string Validation = "validation";

    //private readonly JsonSerializerOptions _options;
    private readonly JsonSerializerOptions _options;

    public RtRepositorySerializer()
    {
        _options = new JsonSerializerOptions
        {
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters =
            {
                new CkIdTypeIdConverter(), new CkIdAssociationRoleIdConverter()
            }
        };
    }

    public async Task SerializeAsync(StreamWriter streamWriter, IEnumerable<RtEntity> collection)
    {
        var rtEntityJsons = collection.Select(e => new RtEntityJson
        {
            RtId = e.RtId,
            CkTypeId = e.CkTypeId ?? throw PersistenceException.CkTypeIdNotSet(),
            RtCreationDateTime = e.RtCreationDateTime,
            RtChangedDateTime = e.RtChangedDateTime,
            RtWellKnownName = e.RtWellKnownName,
            Attributes = new Dictionary<string, object?>(e.Attributes)
        });

        await JsonSerializer.SerializeAsync(streamWriter.BaseStream, rtEntityJsons, _options).ConfigureAwait(false);
    }

    public async Task SerializeAsync(StreamWriter streamWriter, IEnumerable<RtAssociation> collection)
    {
        var associationJsons = collection.Select(e => new RtAssociationJson
        {
            AssociationId = e.AssociationId,
            OriginRtId = e.OriginRtId,
            OriginCkTypeId = e.OriginCkTypeId ?? throw PersistenceException.CkTypeIdNotSet(),
            TargetRtId = e.TargetRtId,
            TargetCkTypeId = e.TargetCkTypeId ?? throw PersistenceException.CkTypeIdNotSet(),
            AssociationRoleId = e.AssociationRoleId ?? throw PersistenceException.AssociationRoleIdNotSet(),
            Attributes = new Dictionary<string, object?>(e.Attributes)
        });

        await JsonSerializer.SerializeAsync(streamWriter.BaseStream, associationJsons, _options).ConfigureAwait(false);
    }

    public async Task<IEnumerable<RtEntity>> DeserializeEntitiesAsync(Stream stream, string locationReference,
        OperationResult operationResult)
    {
        try
        {
            var rtEntities = await JsonSerializer.DeserializeAsync<IEnumerable<RtEntityJson>>(stream, _options).ConfigureAwait(false);
            if (rtEntities == null)
            {
                throw RuntimeModelParseException.CannotDeserializeModel(operationResult);
            }

            return rtEntities.Select(e =>
            {
                var entity = new RtEntity(e.CkTypeId ?? throw PersistenceException.CkTypeIdNotSet(), e.RtId, e.Attributes.ToDictionary(k => k.Key.ToPascalCase(), v => v.Value));
                return entity;
            });
        }
        catch (JsonException e)
        {
            CheckException(locationReference, operationResult, e);
            throw RuntimeModelParseException.CannotDeserializeModel(operationResult);
        }
    }

    public async Task<IEnumerable<RtAssociation>> DeserializeAssociationsAsync(Stream stream, string locationReference,
        OperationResult operationResult)
    {
        try
        {
            var rtAssociations = await JsonSerializer.DeserializeAsync<IEnumerable<RtAssociation>>(stream, _options).ConfigureAwait(false);
            return rtAssociations ?? throw RuntimeModelParseException.CannotDeserializeModel(operationResult);
        }
        catch (JsonException e)
        {
            CheckException(locationReference, operationResult, e);
            throw RuntimeModelParseException.CannotDeserializeModel(operationResult);
        }
    }

    private static void CheckException(string locationReference, OperationResult operationResult, JsonException e)
    {
        if (e.Data.Contains(Validation))
        {
            var evaluationResults = (EvaluationResults?)e.Data[Validation];
            if (evaluationResults != null)
            {
                if (!ValidateEvaluationResults(locationReference, operationResult, evaluationResults))
                {
                    throw RuntimeModelParseException.SchemaValidationFailed(locationReference, operationResult);
                }
            }
        }
    }

    private static bool ValidateEvaluationResults(string locationReference, OperationResult operationResult,
        EvaluationResults evaluationResults)
    {
        if (!evaluationResults.IsValid)
        {
            foreach (var evaluationResult in evaluationResults.Details.Where(x => x.HasErrors))
            {
                var path = evaluationResult.InstanceLocation.ToString();
                var errorMessages = string.Join(", ", evaluationResults.Errors?.Values ?? Enumerable.Empty<string>());
                operationResult.AddMessage(MessageCodes.SchemaValidationError(locationReference, path, errorMessages));
            }
        }

        return evaluationResults.IsValid;
    }

    private record RtEntityJson
    {
        public CkId<CkTypeId>? CkTypeId { get; set; }
        public OctoObjectId RtId { get; set; }
        public DateTime? RtCreationDateTime { get; set; }
        public DateTime? RtChangedDateTime { get; set; }
        public string? RtWellKnownName { get; set; }
        public Dictionary<string, object?> Attributes { get; set; } = new();
    }

    private record RtAssociationJson
    {
        public OctoObjectId AssociationId { get; set; }
        public OctoObjectId OriginRtId { get; set; }
        public CkId<CkTypeId> OriginCkTypeId { get; set; } = null!;
        public OctoObjectId TargetRtId { get; set; }
        public CkId<CkTypeId> TargetCkTypeId { get; set; } = null!;
        public CkId<CkAssociationRoleId> AssociationRoleId { get; set; } = null!;

        public Dictionary<string, object?> Attributes { get; set; } = new();
    }
}