using Meshmakers.Octo.Runtime.Contracts.Geospatial.Geometry;

namespace Meshmakers.Octo.Runtime.Contracts.Geospatial;

internal static class PositionExtensions
{
    internal static Position ToPosition(this IEnumerable<double> coordinates)
    {
        using var enumerator = coordinates.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            throw new ArgumentException("Expected 2 or 3 coordinates but got 0");
        }
        var lng = enumerator.Current;
        if (!enumerator.MoveNext())
        {
            throw new ArgumentException("Expected 2 or 3 coordinates but got 1");
        }
        var lat = enumerator.Current;
        if (!enumerator.MoveNext())
        {
            return new Position(lat, lng);
        }
        var alt = enumerator.Current;
        if (enumerator.MoveNext())
        {
            throw new ArgumentException("Expected 2 or 3 coordinates but got >= 4");
        }
        return new Position(lat, lng, alt);
    }
}