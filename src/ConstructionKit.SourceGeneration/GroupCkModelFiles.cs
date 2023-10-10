namespace Meshmakers.Octo.ConstructionKit.SourceGeneration;

internal static class GroupCkModelFiles
{
    public static IEnumerable<GroupedModelFile> Group(IReadOnlyList<AdditionalTextWithHash> allFilesWithHash,
        CancellationToken cancellationToken = default)
    {
        var lookup = new Dictionary<Tuple<string, string>, AdditionalTextWithHash>();
        
        var res = new Dictionary<AdditionalTextWithHash, AdditionalTextWithHash>();
        foreach (var file in allFilesWithHash)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = file.File.Path;
            var directoryPath = Path.GetDirectoryName(path)?.ToLower() ?? "";
            var baseName = Path.GetFileNameWithoutExtension(path).ToLower();
            if (!baseName.Contains("cache"))
            {
                //it should be impossible to exist already, but VS sometimes throws error about duplicate key added. Keep the original entry, not the new one
                lookup.TryAdd(new Tuple<string, string>(directoryPath, baseName), file);
            }
        }

        foreach (var fileWithHash in allFilesWithHash)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = fileWithHash.File.Path;
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path).ToLower();
            var baseName = fileNameWithoutExtension.Replace(".cache", "");
            var directoryPath = Path.GetDirectoryName(path)?.ToLower() ?? "";
            var lookupTuple = new Tuple<string, string>(directoryPath, baseName);
            if (fileNameWithoutExtension.Contains("cache") && lookup.TryGetValue(lookupTuple, out var mainFile))
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