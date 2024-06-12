using System.Text;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

internal class InheritanceHelpers(ILinkHelpers linkHelpers)
{
    internal async Task AddHierarchy(StreamWriter outputFile, CkTypeGraph ckTypeGraph, string baseRelativePath)
    {
        var hierarchy = ReconstructHierarchyFromPath(ckTypeGraph.Path, baseRelativePath);
        await outputFile.WriteLineAsync($"**Inheritance:** {hierarchy}").ConfigureAwait(false);
    }

    private string ReconstructHierarchyFromPath(string path, string baseRelativePath)
    {
        string[] separators = ["->", ":"];

        var parts = path.Split(separators, StringSplitOptions.None).Select(s => s.Trim()).ToArray();

        var reconstructedHierarchy = parts.Reverse();

        return BuildHierarchyString(reconstructedHierarchy.ToArray(), baseRelativePath);
    }

    private string BuildHierarchyString(string[] reconstructedHierarchy, string baseRelativePath)
    {
        StringBuilder stringBuilder = new();

        for (var i = 0; i < reconstructedHierarchy.Length; i++)
        {
            var obj = reconstructedHierarchy[i];
            var builder = new LinkItemBuilder(obj, baseRelativePath, linkHelpers);
            builder.BuildLinkToType();
            stringBuilder.Append(builder);

            if (i < reconstructedHierarchy.Length - 1)
            {
                stringBuilder.Append(' ')
                    .Append('\u2794')
                    .Append(' ');
            }
        }

        return stringBuilder.ToString();
    }
}