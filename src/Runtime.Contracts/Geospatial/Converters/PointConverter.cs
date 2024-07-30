using Meshmakers.Common.Shared;
using Meshmakers.Octo.Runtime.Contracts.Geospatial.CoordinateReferenceSystem;
using Meshmakers.Octo.Runtime.Contracts.Geospatial.Geometry;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.Runtime.Contracts.Geospatial.Converters;

internal class PointConverter : IYamlTypeConverter
{
    private readonly PositionConverter _positionConverter = new();
    private readonly CrsConverter _crsConverter = new();

    public bool Accepts(Type type)
    {
        return typeof(Point).IsAssignableFrom(type);
    }

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        IPosition? position = null;
        ICRSObject? crsObject = null;

        parser.Consume<MappingStart>();
        while (!parser.TryConsume<MappingEnd>(out _))
        {
            var propertyName = parser.Consume<Scalar>();
            switch (propertyName.Value.ToPascalCase())
            {
                case nameof(Point.Coordinates):
                    position = (IPosition)_positionConverter.ReadYaml(parser, typeof(IPosition), rootDeserializer);
                    break;
                case nameof(Point.Type):
                    var valueScalar = parser.Consume<Scalar>();
                    if (!Enum.TryParse<GeoJSONObjectType>(valueScalar.Value, ignoreCase: true, out var result))
                    {
                        throw RuntimeModelParseException.InvalidEnumValue<GeoJSONObjectType>(valueScalar.Value);
                    }

                    if (result != GeoJSONObjectType.Point)
                    {
                        throw RuntimeModelParseException.InvalidExpectedEnumValue(result, GeoJSONObjectType.Point);
                    }

                    break;
                default:
                    if (propertyName.Value == "crs")
                    {
                        crsObject = (ICRSObject?)_crsConverter.ReadYaml(parser, typeof(ICRSObject), rootDeserializer);
                    }
                    break;
            }
        }

        if (position == null)
        {
            throw RuntimeModelParseException.MissingProperty(nameof(Point), nameof(Point.Coordinates));
        }

        return new Point(position)
        {
            CRS = crsObject
        };
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer rootSerializer)
    {
        throw RuntimeModelParseException.NotImplemented();
    }
}