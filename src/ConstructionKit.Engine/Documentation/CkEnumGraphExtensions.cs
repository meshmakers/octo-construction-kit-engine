using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

internal static class CkEnumGraphExtensions
{
    public static async Task DrawEnum(this CkEnumGraph ckEnumGraph, StreamWriter outputFile)
    {
        var counter = 0;
        foreach (var value in ckEnumGraph.Values)
        {
            var escapedDescription = TextWrapper.EscapeMdxSpecialCharacters(value.Description);
            await outputFile.WriteLineAsync($"| {counter++} | " +
                                            $"{value.Name} | " +
                                            $"{escapedDescription} |").ConfigureAwait(false);
        }
    }
}