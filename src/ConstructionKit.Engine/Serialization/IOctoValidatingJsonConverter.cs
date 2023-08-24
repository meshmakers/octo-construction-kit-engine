using Json.Schema;

namespace Meshmakers.Octo.ConstructionKit.Engine.Serialization;

internal interface IOctoValidatingJsonConverter
{
    OutputFormat OutputFormat { get; set; }

    bool RequireFormatValidation { get; set; }
}