using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Json.Schema;

namespace Meshmakers.Octo.ConstructionKit.Engine.Serialization;

internal class OctoValidatingJsonConverter<T> : JsonConverter<T>, IOctoValidatingJsonConverter
{
    private readonly JsonSchema _schema;
    private readonly Func<JsonSerializerOptions, JsonSerializerOptions> _optionsFactory;

    public OutputFormat OutputFormat { get; set; }

    public bool RequireFormatValidation { get; set; }

    public OctoValidatingJsonConverter(
        JsonSchema schema,
        Func<JsonSerializerOptions, JsonSerializerOptions> optionsFactory)
    {
        _schema = schema;
        _optionsFactory = optionsFactory;
    }

    public override T? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        Utf8JsonReader reader1 = reader;
        EvaluationResults evaluationResults = _schema.Evaluate(JsonSerializer.Deserialize<JsonNode>(ref reader1, options), new EvaluationOptions
        {
            OutputFormat = OutputFormat,
            RequireFormatValidation = RequireFormatValidation
        });
        if (evaluationResults.IsValid)
        {
            JsonSerializerOptions options1 = _optionsFactory(options);
            return JsonSerializer.Deserialize<T>(ref reader, options1);
        }

        JsonException jsonException = new JsonException("JSON does not meet schema requirements")
        {
            Data =
            {
                ["validation"] = evaluationResults
            }
        };
        throw jsonException;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        JsonSerializerOptions options1 = _optionsFactory(options);
        JsonSerializer.Serialize(writer, value, options1);
    }
}
