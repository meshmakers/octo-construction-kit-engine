using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations.GenerateDocsTools;

internal static class CkEnumGraphExtensions
{
    public static async Task DrawEnum(this CkEnumGraph ckEnumGraph, StreamWriter outputFile)
    {
        var counter = 0;
        foreach (var value in ckEnumGraph.Values)
        {
            await outputFile.WriteLineAsync($"| {counter++} | " +
                                            $"{value.Name} | " +
                                            $"{value.Description} |");
        }
    }
}