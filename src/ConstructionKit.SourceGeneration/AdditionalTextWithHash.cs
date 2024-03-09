using Microsoft.CodeAnalysis;

namespace Meshmakers.Octo.ConstructionKit.SourceGeneration;

public readonly record struct AdditionalTextWithHash(AdditionalText File, string? Hash)
{
    public AdditionalText File { get; } = File;
    public string? Hash { get; } = Hash;

    public bool Equals(AdditionalTextWithHash other)
    {
        return File.Path.Equals(other.File.Path) && Equals(Hash, other.Hash);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (File.GetHashCode() * 397) ^ Hash?.GetHashCode() ?? 0;
        }
    }

    public override string ToString()
    {
        return $"{nameof(File)}: {File?.Path}, {nameof(Hash)}: {Hash}";
    }

    public void Deconstruct(out AdditionalText file, out string? hash)
    {
        file = File;
        hash = Hash;
    }
}