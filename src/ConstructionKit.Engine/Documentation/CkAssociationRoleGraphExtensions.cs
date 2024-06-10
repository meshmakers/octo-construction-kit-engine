using System.Text;
using Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations.GenerateDocsTools;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

internal static class CkAssociationRoleGraphExtensions
{
    public static async Task DrawAssociationRole(this CkAssociationRoleGraph ckAssociationRoleGraph, StreamWriter outputFile, 
        CkTypeAssociationGraph? association, string baseRelativePath, ILinkHelpers linkHelpers)
    {
        await outputFile.WriteLineAsync($"| {ckAssociationRoleGraph.AddAnchor() +
                                             ckAssociationRoleGraph.DrawLinkToDefinition(baseRelativePath, linkHelpers)} | " +
                                        $"{ckAssociationRoleGraph.InboundMultiplicity} | " +
                                        $"{ckAssociationRoleGraph.InboundName} | " +
                                        $"{ckAssociationRoleGraph.OutboundMultiplicity} | " +
                                        $"{ckAssociationRoleGraph.OutboundName} | " +
                                        $"{association?.TargetCkTypeId.SemanticVersionedFullName} | " +
                                        $"{association?.DrawTargetAttributes()} |").ConfigureAwait(false);
    }

    private static string DrawLinkToDefinition(this CkAssociationRoleGraph ckAssociationRoleGraph, string baseRelativePath, 
        ILinkHelpers linkHelpers)
    {
        var builder = new LinkItemBuilder(ckAssociationRoleGraph.CkRoleId.SemanticVersionedFullName, baseRelativePath, linkHelpers);
        builder.BuildLinkToAssociation();
        return builder.ToString();
    }

    private static string AddAnchor(this CkAssociationRoleGraph ckAssociationRoleGraph)
    {
        return $"<a id=\"{ckAssociationRoleGraph.CkRoleId.SemanticVersionedFullName}\"></a>";
    }

    private static string DrawTargetAttributes(this CkTypeAssociationGraph? ckTypeAssociationGraph)
    {
        if (ckTypeAssociationGraph?.TargetAttributes == null) return "";
        StringBuilder stringBuilder = new();
        stringBuilder.Append("<ul style={{ listStyleType: \"none\" }}>");


        foreach (var attribute in ckTypeAssociationGraph.TargetAttributes)
        {
            stringBuilder.Append("<li>");
            stringBuilder.Append($"{attribute}");
            stringBuilder.Append("</li>");
        }

        stringBuilder.Append("</ul>");
        return stringBuilder.ToString();

    }
}