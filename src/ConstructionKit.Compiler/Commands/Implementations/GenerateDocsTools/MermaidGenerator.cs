using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations.GenerateDocsTools;

public class MermaidGenerator(IDirectoryTools directoryTools, ILinkHelpers linkHelpers) : IMermaidGenerator
{

    public async Task GenerateMermaidTextOutput(CkModelGraph modelGraph, string documentPath, CkModelId ckModelId)
    {
        directoryTools.BuildDirectory(documentPath, ckModelId);
        var baseRelativePath = directoryTools.GetRelativeDestinationDirectory(documentPath);

        await using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "index"));

        //Create Page Heading
        var split = ckModelId.SemanticVersionedFullName.Split('.');
        await outputFile.WriteLineAsync($"# {split.Last()}");
        await outputFile.WriteLineAsync();

        await TextWrapper.AddDescription(outputFile, "DIAGRAM DESCRIPTION");

        await GenerateMermaidBoilerplate(ckModelId.SemanticVersionedFullName, outputFile);

        //Prints Class and Defined Attributes of Each Type if there is any
        foreach (var type in GetValues.GetTypes(modelGraph))
        {
            await type.DrawClass(outputFile);
            await type.DrawInheritance(outputFile);
            await type.DrawAssociations(outputFile, modelGraph.AssociationRoles.Select(x => x.Value));
            await type.LinkToType(outputFile, baseRelativePath, linkHelpers);
            await type.DrawNamespaces(outputFile);
        }

        //final line to end mermaid code block
        await EndDiagram(outputFile);

        await LinkToVersionHistory(outputFile, baseRelativePath);
    }

    private static async Task EndDiagram(StreamWriter outputFile)
    {
        await outputFile.WriteLineAsync("```");
    }

    private static async Task GenerateMermaidBoilerplate(string classDiagramTitle, StreamWriter outputFile)
    {
        //Boilerplate Instructions for Mermaid
        await outputFile.WriteLineAsync("```mermaid"); //Start Mermaid Code Block
        await outputFile.WriteLineAsync("---"); //Diagram Title Syntax
        await outputFile.WriteLineAsync($"title: {classDiagramTitle}");
        await outputFile.WriteLineAsync("---");
        await outputFile.WriteLineAsync("classDiagram"); //Diagram Type
        await outputFile.WriteLineAsync("direction BT"); //Diagram Direction: Options TB, BT, RL, LR
    }

    private async Task LinkToVersionHistory(StreamWriter outputFile, string baseRelativePath)
    {
        var builder = new LinkItemBuilder("VersionHistory", baseRelativePath, linkHelpers);
        builder.BuildLinkToVersionHistory();
        await outputFile.WriteLineAsync(builder.ToString());
    }
}