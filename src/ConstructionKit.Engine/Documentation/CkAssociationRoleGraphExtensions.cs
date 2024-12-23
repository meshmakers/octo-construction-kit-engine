using System.Text;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

internal static class CkAssociationRoleGraphExtensions
{
    public static async Task DrawAssociationRole(this CkAssociationRoleGraph ckAssociationRoleGraph, StreamWriter outputFile, 
        CkTypeAssociationGraph? association, string baseRelativePath, IDirectoryTools directoryTools, ILinkHelpers linkHelpers)
    { 
       await outputFile.WriteLineAsync("| Property | Value |").ConfigureAwait(false);
       await outputFile.WriteLineAsync("| -----------| -----------|").ConfigureAwait(false);
       await outputFile.WriteLineAsync($"| InboundMultiplicity | {ckAssociationRoleGraph.InboundMultiplicity} |").ConfigureAwait(false);
       await outputFile.WriteLineAsync($"| InboundName | {ckAssociationRoleGraph.InboundName} |").ConfigureAwait(false);
       await outputFile.WriteLineAsync($"| OutboundMultiplicity | {ckAssociationRoleGraph.OutboundMultiplicity} |").ConfigureAwait(false);
       await outputFile.WriteLineAsync($"| OutboundName | {ckAssociationRoleGraph.OutboundName} |").ConfigureAwait(false);
       if (association != null)
       {
           await outputFile.WriteLineAsync($"| TargetCkTypeId | {ckAssociationRoleGraph.OutboundName} |").ConfigureAwait(false);
           if (association.TargetAttributes != null)
           {
               await outputFile.WriteLineAsync($"| TargetAttributes | {association.DrawTargetAttributes()} |").ConfigureAwait(false);
           }
       }
       
       await outputFile.WriteLineAsync($"#### Diagram").ConfigureAwait(false);
      
       // Diagram to show the association role  
       await new MermaidGenerator(directoryTools, linkHelpers)
           .GenerateAssociationGraph(outputFile, ckAssociationRoleGraph)
           .ConfigureAwait(false);
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