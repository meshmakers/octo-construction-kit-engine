using System.Text;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

internal static class CkAssociationRoleGraphExtensions
{
    public static async Task DrawAssociationRole(this CkAssociationRoleGraph ckAssociationRoleGraph,
        StreamWriter outputFile, IDirectoryTools directoryTools, ILinkHelpers linkHelpers)
    {
        await outputFile.WriteLineAsync("| Property | Value |").ConfigureAwait(false);
        await outputFile.WriteLineAsync("| -----------| -----------|").ConfigureAwait(false);
        await outputFile.WriteLineAsync($"| InboundMultiplicity | {ckAssociationRoleGraph.InboundMultiplicity} |")
            .ConfigureAwait(false);
        await outputFile.WriteLineAsync($"| InboundName | {ckAssociationRoleGraph.InboundName} |")
            .ConfigureAwait(false);
        await outputFile.WriteLineAsync($"| OutboundMultiplicity | {ckAssociationRoleGraph.OutboundMultiplicity} |")
            .ConfigureAwait(false);
        await outputFile.WriteLineAsync($"| OutboundName | {ckAssociationRoleGraph.OutboundName} |")
            .ConfigureAwait(false);

        await outputFile.WriteLineAsync($"#### Diagram").ConfigureAwait(false);

        // Diagram to show the association role  
        await new MermaidGenerator(directoryTools, linkHelpers)
            .GenerateAssociationGraph(outputFile, ckAssociationRoleGraph)
            .ConfigureAwait(false);
    }

    public static async Task DrawTypeAssociation(this CkTypeAssociationGraph association, GraphDirections direction,
        StreamWriter outputFile, string baseRelativePath, ILinkHelpers linkHelpers)
    {
        await outputFile.WriteAsync($"| {association.NavigationPropertyName} |")
            .ConfigureAwait(false);
        await outputFile.WriteAsync($"{association.Multiplicity} |")
            .ConfigureAwait(false);
        
        var builderAssoc = new LinkItemBuilder(association.CkRoleId.SemanticVersionedFullName, baseRelativePath, linkHelpers);
        builderAssoc.BuildLinkToAssociation();
        await outputFile.WriteAsync($"{builderAssoc} |")
            .ConfigureAwait(false);
        
        if (direction == GraphDirections.Outbound)
        {
            var builder = new LinkItemBuilder(association.TargetCkTypeId.SemanticVersionedFullName, baseRelativePath, linkHelpers);
            builder.BuildLinkToType();
            await outputFile.WriteAsync($"{builder} |")
                .ConfigureAwait(false);   
        }
        else
        {
            var builder = new LinkItemBuilder(association.OriginCkTypeId.SemanticVersionedFullName, baseRelativePath, linkHelpers);
            builder.BuildLinkToType();
            await outputFile.WriteAsync($"{builder} |")
                .ConfigureAwait(false);
        }

        if (association.TargetAttributes != null)
        {
            await outputFile.WriteAsync($"{association.DrawTargetAttributes()} |")
                .ConfigureAwait(false);
        }
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