using Microsoft.CodeAnalysis;

namespace Meshmakers.Octo.ConstructionKit.SourceGeneration;

public readonly record struct AdditionalTextWithHash
{
    public AdditionalTextWithHash(AdditionalText file, string hash)
    {
        File = file;
        Hash = hash;
    }

    public AdditionalText File { get; }
    public string Hash { get; }

    public bool Equals(AdditionalTextWithHash other)
    {
        return File.Path.Equals(other.File.Path) && Hash.Equals(other.Hash);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (File.GetHashCode() * 397) ^ Hash.GetHashCode();
        }
    }

    public override string ToString()
    {
        return $"{nameof(File)}: {File?.Path}, {nameof(Hash)}: {Hash}";
    }

    public void Deconstruct(out AdditionalText file, out string hash)
    {
        file = File;
        hash = Hash;
    }
}