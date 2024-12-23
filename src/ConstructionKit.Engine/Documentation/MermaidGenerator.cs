using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

internal class MermaidGenerator(IDirectoryTools directoryTools, ILinkHelpers linkHelpers) : IMermaidGenerator
{
    public async Task GenerateAssociationGraph(StreamWriter outputFile, CkAssociationRoleGraph ckAssociationRoleGraph)
    {
        await GenerateMermaidHeading(ckAssociationRoleGraph.CkRoleId.SemanticVersionedFullName, outputFile)
            .ConfigureAwait(false);

        await outputFile.WriteLineAsync("classDiagram").ConfigureAwait(false);
        await outputFile.WriteLineAsync("direction LR").ConfigureAwait(false);

        await outputFile.WriteLineAsync("class Target[\"Target (Inbound)\"] {").ConfigureAwait(false);
        await outputFile.WriteLineAsync($"  {ckAssociationRoleGraph.InboundName}").ConfigureAwait(false);
        await outputFile.WriteLineAsync("}").ConfigureAwait(false);
        await outputFile.WriteLineAsync("class Source[\"Source (Outbound)\"]  {").ConfigureAwait(false);
        await outputFile.WriteLineAsync($"  {ckAssociationRoleGraph.OutboundName}").ConfigureAwait(false);
        await outputFile.WriteLineAsync("}").ConfigureAwait(false);
     //   await outputFile.WriteLineAsync("note for Source \"Association defined here\"").ConfigureAwait(false);

        string outboundMultiplicity = GetMultiplicity(ckAssociationRoleGraph.OutboundMultiplicity);
        string inboundMultiplicity = GetMultiplicity(ckAssociationRoleGraph.InboundMultiplicity);
        string associationRepresentation = GetAssociationRepresentation(ckAssociationRoleGraph);


        string t = $"Source \"{inboundMultiplicity}\"" +
                   $" {associationRepresentation} \"{outboundMultiplicity}\" Target";
        await outputFile.WriteLineAsync(t).ConfigureAwait(false);

        //final line to end mermaid code block
        await EndDiagram(outputFile).ConfigureAwait(false);
        
        await outputFile.WriteLineAsync("Source entity defines the association").ConfigureAwait(false);
    }

    public async Task GenerateMermaidTextOutput(CkModelGraph modelGraph, string documentPath, CkModelId ckModelId,
        string? versionNumber,
        string linkPathRoot)
    {
        directoryTools.BuildDirectory(documentPath, ckModelId);
#if NETSTANDARD2_0
        using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "index"));
#else
        await using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "index"));
#endif

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

        var modelDescription = GetModelDescription(modelGraph, ckModelId);

        if (modelDescription != null)
        {
            await TextWrapper.AddDescription(outputFile, modelDescription).ConfigureAwait(false);
        }

        await GenerateMermaidHeading(ckModelId.SemanticVersionedFullName, outputFile).ConfigureAwait(false);

        //Prints Class and Defined Attributes of Each Type if there is any
        await GenerateMermaidDiagram(modelGraph, documentPath, ckModelId, outputFile, linkPathRoot)
            .ConfigureAwait(false);

        //final line to end mermaid code block
        await EndDiagram(outputFile).ConfigureAwait(false);

        await LinkToVersionHistory(outputFile, linkPathRoot).ConfigureAwait(false);
    }

    private static string? GetModelDescription(CkModelGraph modelGraph, CkModelId ckModelId)
    {
        var modelDescription = "";
        foreach (var model in modelGraph.Models)
        {
            if (model.Value.ModelId == ckModelId)
            {
                modelDescription = model.Value.Description;
            }
        }

        return modelDescription;
    }
    
    private string GetAssociationRepresentation(CkAssociationRoleGraph ckAssociationRoleGraph)
    {
        return ckAssociationRoleGraph.OutboundMultiplicity switch
        {
            MultiplicitiesDto.One => "--*",
            _ => "-->"
        };
    }
    
    private string GetMultiplicity(MultiplicitiesDto multiplicities)
    {
        return multiplicities switch
        {
            MultiplicitiesDto.ZeroOrOne => "0..1",
            MultiplicitiesDto.One => "1",
            MultiplicitiesDto.N => "n",
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public async Task GenerateMermaidDiagram(CkModelGraph modelGraph, string documentPath, CkModelId ckModelId,
        StreamWriter outputFile,
        string linkPathRoot)
    {
        directoryTools.BuildDirectory(documentPath, ckModelId);

        await GenerateMermaidInstructions(outputFile).ConfigureAwait(false);

        //This setup ensures that all relevant Namespaces and Classes are added to the Mermaid diagram
        var drawnTypes = new HashSet<CkId<CkTypeId>>();
        var externalTypes = new HashSet<CkId<CkTypeId>>();
        var associatedTypes = new HashSet<CkId<CkTypeId>>();

        foreach (var type in modelGraph.GetTypes())
        {
            if (type.CkTypeId.ModelId != ckModelId) continue;
            await DrawTypeOfModel(modelGraph, outputFile, linkPathRoot, type, drawnTypes, externalTypes,
                associatedTypes).ConfigureAwait(false);
        }

        foreach (var externalType in externalTypes)
        {
            modelGraph.Types.TryGetValue(externalType, out var baseTypeGraph);
            await baseTypeGraph!.DrawExternal(outputFile, linkPathRoot, linkHelpers).ConfigureAwait(false);
        }

        foreach (var associatedType in associatedTypes)
        {
            modelGraph.Types.TryGetValue(associatedType, out var baseAssociatedTypes);
            await baseAssociatedTypes!.DrawExternal(outputFile, linkPathRoot, linkHelpers).ConfigureAwait(false);
        }
    }

    private async Task DrawTypeOfModel(CkModelGraph modelGraph, StreamWriter outputFile, string linkPathRoot,
        CkTypeGraph type, HashSet<CkId<CkTypeId>> drawnTypes, HashSet<CkId<CkTypeId>> externalTypes,
        HashSet<CkId<CkTypeId>> associatedTypes)
    {
        if (drawnTypes.Contains(type.CkTypeId)) return;

        await type.DrawClass(outputFile).ConfigureAwait(false);
        await type.DrawInheritance(outputFile).ConfigureAwait(false);
        await type.DrawAssociations(outputFile, modelGraph.AssociationRoles.Select(x => x.Value)).ConfigureAwait(false);
        await type.LinkToType(outputFile, linkPathRoot, linkHelpers).ConfigureAwait(false);
        await type.DrawNamespaces(outputFile).ConfigureAwait(false);
        drawnTypes.Add(type.CkTypeId);
        foreach (var baseType in type.BaseTypes.Where(bt => bt.BaseTypeDepthIndex == 0))
        {
            externalTypes.Add(baseType.BaseCkTypeId);
        }

        foreach (var ownedAssociation in type.Associations.Out.Owned)
        {
            associatedTypes.Add(ownedAssociation.TargetCkTypeId);
        }
    }

    //For Library Use
    //Has not implemented new diagram generation!
    public async Task GenerateMermaidDiagram(CkModelGraph modelGraph, string outputPath)
    {
#if NETSTANDARD2_0
        using StreamWriter outputFile = new(outputPath);
#else
        await using StreamWriter outputFile = new(outputPath);
#endif


        await GenerateMermaidInstructions(outputFile).ConfigureAwait(false);

        foreach (var type in modelGraph.GetTypes())
        {
            await type.DrawClass(outputFile).ConfigureAwait(false);
            await type.DrawInheritance(outputFile).ConfigureAwait(false);
            await type.DrawAssociations(outputFile, modelGraph.AssociationRoles.Select(x => x.Value))
                .ConfigureAwait(false);
            await type.DrawNamespaces(outputFile).ConfigureAwait(false);
        }
    }

    private static async Task EndDiagram(StreamWriter outputFile)
    {
        await outputFile.WriteLineAsync("```").ConfigureAwait(false);
    }

    private static async Task GenerateMermaidHeading(StreamWriter outputFile)
    {
        await outputFile.WriteLineAsync("```mermaid").ConfigureAwait(false); //Start Mermaid Code Block
    }

    private static async Task GenerateMermaidHeading(string classDiagramTitle, StreamWriter outputFile)
    {
        //Boilerplate Instructions for Mermaid
        await GenerateMermaidHeading(outputFile).ConfigureAwait(false);
        await outputFile.WriteLineAsync("---").ConfigureAwait(false); //Diagram Title Syntax
        await outputFile.WriteLineAsync($"title: {classDiagramTitle}").ConfigureAwait(false);
        await outputFile.WriteLineAsync("---").ConfigureAwait(false);
    }

    private static async Task GenerateMermaidInstructions(StreamWriter outputFile)
    {
        await outputFile.WriteLineAsync("classDiagram").ConfigureAwait(false); //Diagram Type
        await outputFile.WriteLineAsync("direction BT")
            .ConfigureAwait(false); //Diagram Direction: Options TB, BT, RL, LR
    }

    private async Task LinkToVersionHistory(StreamWriter outputFile, string baseRelativePath)
    {
        var builder = new LinkItemBuilder("VersionHistory", baseRelativePath, linkHelpers);
        builder.BuildLinkToVersionHistory();
        await outputFile.WriteLineAsync(builder.ToString()).ConfigureAwait(false);
    }
}