namespace Meshmakers.Octo.ConstructionKit.SourceGeneration;

internal static class GroupCkModelFiles
{
    public static IEnumerable<GroupedModelFile> Group(IReadOnlyList<AdditionalTextWithHash> allFilesWithHash,
        CancellationToken cancellationToken = default)
    {
        var lookup = new Dictionary<string, AdditionalTextWithHash>();

        foreach (var file in allFilesWithHash)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = file.File.Path;
            var baseName = Path.GetFileNameWithoutExtension(path).ToLower();
            if (!baseName.Contains("cache"))
            {
                //it should be impossible to exist already, but VS sometimes throws error about duplicate key added. Keep the original entry, not the new one
                if (!lookup.ContainsKey(baseName))
                {
                    lookup.Add(baseName, file);
                }
            }
        }

        var res = new Dictionary<AdditionalTextWithHash, AdditionalTextWithHash>();
        foreach (var fileWithHash in allFilesWithHash)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = fileWithHash.File.Path;
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path).ToLower();
            var baseName = fileNameWithoutExtension.Replace(".cache", "");
            if (fileNameWithoutExtension.Contains("cache") && lookup.TryGetValue(baseName, out var mainFile))
            {
                res.Add(mainFile, fileWithHash);
            }
        }

        // dont care at all HOW it is sorted, just that end result is the same
        foreach (var file in res)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return new GroupedModelFile(file.Key, file.Value);
        }
    }
}