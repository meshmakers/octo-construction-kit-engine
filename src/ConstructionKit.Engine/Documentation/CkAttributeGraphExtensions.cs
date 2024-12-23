using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

internal static class CkAttributeGraphExtensions
{
    internal static string LinkToRecordOrEnum(this CkAttributeGraph ckAttributeGraph, string baseRelativePath, ILinkHelpers linkHelpers)
    {
        if (ckAttributeGraph.ValueCkEnumId != null)
        {
            var builder = new LinkItemBuilder(ckAttributeGraph.ValueCkEnumId.SemanticVersionedFullName, baseRelativePath, linkHelpers);
            builder.BuildLinkToEnum();
            return builder.ToString();
        }

        if (ckAttributeGraph.ValueCkRecordId != null) 
        {
            var builder = new LinkItemBuilder(ckAttributeGraph.ValueCkRecordId.SemanticVersionedFullName, baseRelativePath, linkHelpers);
            builder.BuildLinkToRecord();
            return builder.ToString();
        }
        return "";
    }
}