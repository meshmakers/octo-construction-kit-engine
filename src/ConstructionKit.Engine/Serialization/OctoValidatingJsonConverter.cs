using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Json.Schema;

namespace Meshmakers.Octo.ConstructionKit.Engine.Serialization;

internal class OctoValidatingJsonConverter<T> : JsonConverter<T>, IOctoValidatingJsonConverter
{
    private readonly Func<JsonSerializerOptions, JsonSerializerOptions> _optionsFactory;
    private readonly JsonSchema _schema;

    public OctoValidatingJsonConverter(
        JsonSchema schema,
        Func<JsonSerializerOptions, JsonSerializerOptions> optionsFactory)
    {
        _schema = schema;
        _optionsFactory = optionsFactory;
    }

    public OutputFormat OutputFormat { get; set; }

    public bool RequireFormatValidation { get; set; }

    public override T? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var reader1 = reader;
        // JsonSchema.Net 8.0 requires JsonElement instead of JsonNode
        using var document = JsonDocument.ParseValue(ref reader1);
        var jsonElement = document.RootElement;

        var evaluationResults = _schema.Evaluate(jsonElement, new EvaluationOptions
        {
            OutputFormat = OutputFormat,
            RequireFormatValidation = RequireFormatValidation
        });
        if (evaluationResults.IsValid)
        {
            var options1 = _optionsFactory(options);
            return JsonSerializer.Deserialize<T>(ref reader, options1);
        }

        var jsonException = new JsonException("JSON does not meet schema requirements")
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
        var options1 = _optionsFactory(options);
        JsonSerializer.Serialize(writer, value, options1);
    }
}