namespace Meshmakers.Octo.ConstructionKit.SourceGeneration;

internal readonly record struct GroupedModelFile
{
    public GroupedModelFile(AdditionalTextWithHash mainFile, AdditionalTextWithHash cacheFile)
    {
        MainFile = mainFile;
        CacheFile = cacheFile;
    }

    public bool Equals(GroupedModelFile other)
    {
        return MainFile.Equals(other.MainFile) && CacheFile.Equals(other.CacheFile);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hashCode = MainFile.GetHashCode();
            hashCode = (hashCode * 397) ^ CacheFile.GetHashCode();

            return hashCode;
        }
    }

    public override string ToString()
    {
        return
            $"{nameof(MainFile)}: {MainFile}, {nameof(CacheFile)}: {CacheFile}";
    }

    public AdditionalTextWithHash MainFile { get; }
    public AdditionalTextWithHash CacheFile { get; }
}