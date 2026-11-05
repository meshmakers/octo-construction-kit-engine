using System.Runtime.CompilerServices;
using Meshmakers.Common.Shared;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Runtime.Engine.Serialization;

internal class RtDeserializeStream : IRtDeserializeStream
{
    private readonly List<CkModelIdVersionRange> _dependencies;
    private readonly int _maxCount;
    private readonly JsonTextReader _reader;
    private readonly JsonSerializer _serializer;

    public RtDeserializeStream(Stream stream, int maxCount)
    {
        _reader = new JsonTextReader(new StreamReader(stream));
        _reader.SupportMultipleContent = true;

        _serializer = new JsonSerializer
        {
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            NullValueHandling = NullValueHandling.Ignore
        };

        _maxCount = maxCount;
        _dependencies = [];
        Dependencies = _dependencies;
    }

    public IReadOnlyCollection<CkModelIdVersionRange> Dependencies { get; }


    public event EventHandler<RtDeserializeEventArgs>? BulkDeserialized;

    public async Task ReadAsync(CancellationToken? cancellationToken = null)
    {
        await foreach (var bulk in ReadBulksAsync(cancellationToken ?? CancellationToken.None).ConfigureAwait(false))
        {
            BulkDeserialized?.Invoke(this, new RtDeserializeEventArgs(bulk));
        }
    }

    public async IAsyncEnumerable<IReadOnlyList<RtEntityTcDto>> ReadBulksAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_reader.TokenType != JsonToken.PropertyName || !Equals(_reader.Value, nameof(RtModelRootTcDto.Entities).ToCamelCase()))
        {
            throw RuntimeModelParseException.InvalidPosition();
        }

        // AB#8325: the bulk is handed out and the reader waits until the consumer asks for the next one. The former event
        // path kept the entities of a bulk until a handler had set IsHandled, so a handler that set it late (an async
        // handler after its first await) received every entity again with each further entity.
        var bulk = new List<RtEntityTcDto>();
        var bulkIds = new HashSet<OctoObjectId>();
        while (await _reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_reader.TokenType == JsonToken.EndArray)
            {
                break;
            }

            if (_reader.TokenType != JsonToken.StartObject)
            {
                continue;
            }

            var entity = _serializer.Deserialize<RtEntityTcDto>(_reader);
            if (entity == null)
            {
                throw RuntimeModelParseException.CannotDeserializeEntity(_reader.LineNumber);
            }

            RtJsonRecordValueNormalizer.Normalize(entity);
            if (!bulkIds.Add(entity.RtId))
            {
                throw RuntimeModelParseException.DuplicateEntity(entity.RtId);
            }

            bulk.Add(entity);
            if (bulk.Count >= _maxCount)
            {
                yield return bulk;
                bulk = [];
                bulkIds.Clear();
            }
        }

        if (bulk.Count > 0)
        {
            yield return bulk;
        }
    }

    public void Dispose()
    {
    }

    internal async Task InitializeAsync(CancellationToken? cancellationToken = null)
    {
        var startQueue = new Queue<Tuple<JsonToken, object?>>();
        startQueue.Enqueue(new Tuple<JsonToken, object?>(JsonToken.StartObject, null));
        startQueue.Enqueue(new Tuple<JsonToken, object?>(JsonToken.PropertyName, "$schema"));
        startQueue.Enqueue(new Tuple<JsonToken, object?>(JsonToken.String, "https://schemas.meshmakers.cloud/runtime-model.schema.json"));

        while (await _reader.ReadAsync().ConfigureAwait(false))
        {
            if (startQueue.Count > 0)
            {
                var data = startQueue.Dequeue();
                if (_reader.TokenType == data.Item1 && Equals(_reader.Value, data.Item2))
                {
                    continue;
                }

                throw RuntimeModelParseException.InvalidStructure();
            }

            cancellationToken?.ThrowIfCancellationRequested();

            if (_reader.TokenType == JsonToken.PropertyName && Equals(_reader.Value, nameof(RtModelRootTcDto.Dependencies).ToCamelCase()))
            {
                await ReadDependencies().ConfigureAwait(false);
            }
            else if (_reader.TokenType == JsonToken.PropertyName && Equals(_reader.Value, nameof(RtModelRootTcDto.Entities).ToCamelCase()))
            {
                return; // Positioning on entities array done
            }
        }
    }

    private async Task ReadDependencies()
    {
        while (await _reader.ReadAsync().ConfigureAwait(false))
        {
            if (_reader.TokenType == JsonToken.EndArray)
            {
                return;
            }

            if (_reader.TokenType == JsonToken.StartArray)
            {
                continue;
            }

            if (_reader.TokenType == JsonToken.String)
            {
                var value = Convert.ToString(_reader.Value);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    _dependencies.Add(new CkModelIdVersionRange(value));
                }
            }
        }
    }
}