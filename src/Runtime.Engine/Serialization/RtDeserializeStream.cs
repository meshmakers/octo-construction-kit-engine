using System.Collections.Concurrent;
using Meshmakers.Common.Shared;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Runtime.Engine.Serialization;

internal class RtDeserializeStream : IRtDeserializeStream
{
    private readonly List<CkModelId> _dependencies;
    private readonly ConcurrentDictionary<OctoObjectId, RtEntityTcDto> _deserializedEntities = new();
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

    public IReadOnlyCollection<CkModelId> Dependencies { get; }


    public event EventHandler<RtDeserializeEventArgs>? BulkDeserialized;

    public async Task ReadAsync(CancellationToken? cancellationToken = null)
    {
        if (_reader.TokenType != JsonToken.PropertyName || !Equals(_reader.Value, nameof(RtModelRootTcDto.Entities).ToCamelCase()))
        {
            throw RuntimeModelParseException.InvalidPosition();
        }

        while (await _reader.ReadAsync().ConfigureAwait(false))
        {
            if (_reader.TokenType == JsonToken.EndArray)
            {
                if (_deserializedEntities.Any())
                {
                    BulkDeserialized?.Invoke(this, new RtDeserializeEventArgs(_deserializedEntities.Values.ToArray()));
                    _deserializedEntities.Clear();
                }

                return;
            }

            if (_reader.TokenType == JsonToken.StartArray)
            {
                continue;
            }

            if (_reader.TokenType == JsonToken.StartObject)
            {
                var c = _serializer.Deserialize<RtEntityTcDto>(_reader);
                if (c == null)
                {
                    throw RuntimeModelParseException.CannotDeserializeEntity(_reader.LineNumber);
                }

                AddEntity(c);
            }
        }
    }

    public void Dispose()
    {
    }

    private void AddEntity(RtEntityTcDto rtEntityDto)
    {
        if (!_deserializedEntities.TryAdd(rtEntityDto.RtId, rtEntityDto))
        {
            throw RuntimeModelParseException.DuplicateEntity(rtEntityDto.RtId);
        }

        if (_deserializedEntities.Count >= _maxCount)
        {
            var entities = _deserializedEntities.Values.ToArray();
            var rtDeserializeEventArgs = new RtDeserializeEventArgs(entities);
            BulkDeserialized?.Invoke(this, rtDeserializeEventArgs);
            if (rtDeserializeEventArgs.IsHandled)
            {
                foreach (var entityDto in entities)
                {
                    _deserializedEntities.TryRemove(entityDto.RtId, out _);
                }
            }
        }
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
                    _dependencies.Add(new CkModelId(value));
                }
            }
        }
    }
}