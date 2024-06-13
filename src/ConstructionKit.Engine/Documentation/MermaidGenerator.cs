using Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations.GenerateDocsTools;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

internal class MermaidGenerator(IDirectoryTools directoryTools, ILinkHelpers linkHelpers) : IMermaidGenerator
{
    
    public async Task GenerateMermaidTextOutput(CkModelGraph modelGraph, string documentPath, CkModelId ckModelId, string? versionNumber,
        string directoryPath)
    {
        directoryTools.BuildDirectory(documentPath, ckModelId);

        using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "index"));
        
        
        
        //Create Page Heading
        var split = ckModelId.SemanticVersionedFullName.Split('.');
        
        //Newline to fix acorn issue in Docusaurus
        await outputFile.WriteLineAsync("---").ConfigureAwait(false);
        await outputFile.WriteLineAsync($"title: {split.Last()}").ConfigureAwait(false);
        await outputFile.WriteLineAsync("---").ConfigureAwait(false);
        await outputFile.WriteLineAsync().ConfigureAwait(false);
        
        await outputFile.WriteLineAsync($"# {split.Last()}").ConfigureAwait(false);
        await outputFile.WriteLineAsync().ConfigureAwait(false);
        
        if (versionNumber != null)
        {
            await ContentGenerator.AddVersionInfo(outputFile, versionNumber).ConfigureAwait(false);
        }

        await TextWrapper.AddDescription(outputFile, "DIAGRAM DESCRIPTION").ConfigureAwait(false);

        await GenerateMermaidHeading(ckModelId.SemanticVersionedFullName, outputFile).ConfigureAwait(false);

        //Prints Class and Defined Attributes of Each Type if there is any
        await GenerateMermaidDiagram(modelGraph, documentPath, ckModelId, outputFile, directoryPath).ConfigureAwait(false);

        //final line to end mermaid code block
        await EndDiagram(outputFile).ConfigureAwait(false);

        await LinkToVersionHistory(outputFile, directoryPath).ConfigureAwait(false);
    }
    
    public async Task GenerateMermaidDiagram(CkModelGraph modelGraph, string documentPath, CkModelId ckModelId, StreamWriter outputFile,
        string directoryPath)
    {
        directoryTools.BuildDirectory(documentPath, ckModelId);

        await GenerateMermaidInstructions(outputFile).ConfigureAwait(false);
        
        foreach (var type in GetValues.GetTypes(modelGraph))
        {
            await type.DrawClass(outputFile).ConfigureAwait(false);
            await type.DrawInheritance(outputFile).ConfigureAwait(false);
            await type.DrawAssociations(outputFile, modelGraph.AssociationRoles.Select(x => x.Value)).ConfigureAwait(false);
            await type.LinkToType(outputFile, directoryPath, linkHelpers).ConfigureAwait(false);
            await type.DrawNamespaces(outputFile).ConfigureAwait(false);
        }
    }

    //For Library Use
    public async Task GenerateMermaidDiagram(CkModelGraph modelGraph, string outputPath)
    {
        using StreamWriter outputFile = new(outputPath);

        await GenerateMermaidInstructions(outputFile).ConfigureAwait(false);
        
        foreach (var type in GetValues.GetTypes(modelGraph))
        {
            await type.DrawClass(outputFile).ConfigureAwait(false);
            await type.DrawInheritance(outputFile).ConfigureAwait(false);
            await type.DrawAssociations(outputFile, modelGraph.AssociationRoles.Select(x => x.Value)).ConfigureAwait(false);
            await type.DrawNamespaces(outputFile).ConfigureAwait(false);
        }
    }

    private static async Task EndDiagram(StreamWriter outputFile)
    {
        await outputFile.WriteLineAsync("```").ConfigureAwait(false);
    }

    private static async Task GenerateMermaidHeading(string classDiagramTitle, StreamWriter outputFile)
    {
        //Boilerplate Instructions for Mermaid
        await outputFile.WriteLineAsync("```mermaid").ConfigureAwait(false); //Start Mermaid Code Block
        await outputFile.WriteLineAsync("---").ConfigureAwait(false); //Diagram Title Syntax
        await outputFile.WriteLineAsync($"title: {classDiagramTitle}").ConfigureAwait(false);
        await outputFile.WriteLineAsync("---").ConfigureAwait(false);
    }

    private static async Task GenerateMermaidInstructions(StreamWriter outputFile)
    {
        await outputFile.WriteLineAsync("classDiagram").ConfigureAwait(false); //Diagram Type
        await outputFile.WriteLineAsync("direction BT").ConfigureAwait(false); //Diagram Direction: Options TB, BT, RL, LR
    }

    private async Task LinkToVersionHistory(StreamWriter outputFile, string baseRelativePath)
    {
        var builder = new LinkItemBuilder("VersionHistory", baseRelativePath, linkHelpers);
        builder.BuildLinkToVersionHistory();
        await outputFile.WriteLineAsync(builder.ToString()).ConfigureAwait(false);
    }
}