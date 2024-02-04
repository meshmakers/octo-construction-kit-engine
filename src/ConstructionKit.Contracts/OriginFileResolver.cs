namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Resolves the original file location for a given key
/// </summary>
/// <param name="fallbackLocation">A fallback location</param>
public class OriginFileResolver(string fallbackLocation) : IOriginFileResolver
{
    readonly Dictionary<object, string> _locations = new();

    /// <inheritdoc />
    public string Resolve(object key)
    {
        if (!_locations.TryGetValue(key, out string? value) || string.IsNullOrEmpty(value))
        {
            return fallbackLocation;
        }

        return value;
    }

    /// <inheritdoc />
    public void Add(object key, string location)
    {
        _locations[key] = location;
    }
}