using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meshmakers.Octo.Runtime.Contracts.Geospatial.Converters;

internal class JsonCamelCasingStringEnumConverter() : JsonStringEnumConverter(JsonNamingPolicy.CamelCase);