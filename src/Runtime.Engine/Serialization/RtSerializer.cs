using System.Text.Json;
using System.Text.Json.Serialization;
using Meshmakers.Octo.Runtime.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.Runtime.Contracts.Serialization;

/// <summary>
/// Represents the runtime model serializer
/// </summary>
public static class RtSerializer
{
    /// <summary>
    /// Serializes
    /// </summary>
    /// <param name="streamWriter"></param>
    /// <param name="model"></param>
    public static async Task SerializeAsync(StreamWriter streamWriter, RtModelRootDto model)
    {
        var options = new JsonSerializerOptions {  DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };

        await JsonSerializer.SerializeAsync(streamWriter.BaseStream, model, options);
    }

    /// <summary>
    /// Deserializes
    /// </summary>
    /// <param name="textReader"></param>
    /// <param name="entityDeserializedAction"></param>
    /// <param name="cancellationToken"></param>
    public static async Task DeserializeAsync(StreamReader textReader, Func<RtEntityDto, Task> entityDeserializedAction,
        CancellationToken? cancellationToken = null)
    {
        var options = new JsonDocumentOptions {  };
        //
        // var startQueue = new Queue<Tuple<JsonToken, object?>>();
        // startQueue.Enqueue(new Tuple<JsonToken, object?>(JsonToken.StartObject, null));
        // startQueue.Enqueue(new Tuple<JsonToken, object?>(JsonToken.PropertyName, "entities"));
        // startQueue.Enqueue(new Tuple<JsonToken, object?>(JsonToken.StartArray, null));

        using var jsonDocument =await JsonDocument.ParseAsync(textReader.BaseStream, options);
        // while (await jsonDocument.RootElement.)
        {
            // if (startQueue.Count > 0)
            // {
            //     var data = startQueue.Dequeue();
            //     if (reader.TokenType == data.Item1 && Equals(reader.Value, data.Item2))
            //     {
            //         continue;
            //     }
            //
            //     throw new ModelParseException(
            //         "Missing structure of JSON file format. Ensure that file begins with { \"entities\" : [ {");
            // }
            //
            // if (cancellationToken != null && cancellationToken.Value.IsCancellationRequested)
            // {
            //     return;
            // }
            //
            // if (reader.TokenType == JsonToken.StartObject)
            // {
            //     var c = serializer.Deserialize<RtEntity>(reader);
            //     await entityDeserializedAction(c);
            // }
        }
    }
}
