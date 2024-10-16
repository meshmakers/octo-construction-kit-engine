using System.Text;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

internal static class CkAttributeGraphExtensions
{
    public static async Task DrawAttribute(this CkAttributeGraph ckAttributeGraph, StreamWriter outputFile,
        string baseRelativePath, ILinkHelpers linkHelpers)
    {

        await outputFile.WriteLineAsync($"| {ckAttributeGraph.AddAnchor(linkHelpers)}{ckAttributeGraph.AddName()} | " +
                                        $"{ckAttributeGraph.ValueType.ToString()} | " +
                                        $"{ckAttributeGraph.DrawDefaultValues()} | " +
                                        $"{ckAttributeGraph.IsDataStream.ToString()} | " +
                                        $"{ckAttributeGraph.LinkToRecordOrEnum(baseRelativePath, linkHelpers)} |").ConfigureAwait(false);
    }


    private static string AddAnchor(this CkAttributeGraph ckAttributeGraph, ILinkHelpers linkHelpers)
    {
        return $"<a id=\"{linkHelpers.FormatAnchor(ckAttributeGraph.CkAttributeId.Key.SemanticVersionedFullName)}\"></a>";
    }

    private static string AddName(this CkAttributeGraph ckAttributeGraph)
    {
        return $"{ckAttributeGraph.CkAttributeId.SemanticVersionedFullName}";
    }

    private static string DrawDefaultValues(this CkAttributeGraph ckAttributeGraph)
    {
        StringBuilder stringBuilder = new();
        if (ckAttributeGraph.DefaultValues == null) return "";
        foreach (var value in ckAttributeGraph.DefaultValues)
        {
            stringBuilder.Append(value);
        }

        return stringBuilder.ToString();

    }

    private static string LinkToRecordOrEnum(this CkAttributeGraph ckAttributeGraph, string baseRelativePath, ILinkHelpers linkHelpers)
    {
        if (ckAttributeGraph.ValueCkEnumId != null)
        {
            var builder = new LinkItemBuilder(ckAttributeGraph.ValueCkEnumId.SemanticVersionedFullName, baseRelativePath, linkHelpers);
            builder.BuildLinkToEnum();
            return builder.ToString();
        }

        if (ckAttributeGraph.ValueCkRecordId == null) return "";
        {
            var builder = new LinkItemBuilder(ckAttributeGraph.ValueCkRecordId.SemanticVersionedFullName, baseRelativePath, linkHelpers);
            builder.BuildLinkToRecord();
            return builder.ToString();
        }

    }
}