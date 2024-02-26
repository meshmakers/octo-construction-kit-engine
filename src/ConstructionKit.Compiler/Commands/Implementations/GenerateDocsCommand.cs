using System.Globalization;
using System.Runtime.Intrinsics.Arm;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;



static class CkTypeGraphExtensions
{

    public static string GetName(this CkId<CkTypeId> ckTypeGraph) => $"{ckTypeGraph.Key.SemanticVersionedFullName}";
    public static async void DrawClass(this CkTypeGraph ckTypeGraph, StreamWriter outputFile)
    {
        await outputFile.WriteAsync($"class {ckTypeGraph.CkTypeId.GetName()}");

        //for formatting check if attributes defined
        if (ckTypeGraph.DefinedAttributes.Count != 0)
        {
            await outputFile.WriteLineAsync("{");

            foreach (var attribute in ckTypeGraph.DefinedAttributes)
            {
                await outputFile.WriteLineAsync($"+{attribute.AttributeName} : {attribute.AttributeName.GetTypeCode()}");
            }

            await outputFile.WriteLineAsync("}");
        }
        else
        {
            await outputFile.WriteLineAsync();
        }
    }

    public static async void DrawInheritance(this CkTypeGraph ckTypeGraph, StreamWriter outputFile)
    {
        //Checks for Inheritance In BaseTypes and Creates Arrows ;)
        if (ckTypeGraph.BaseTypes.Count != 0)
        {
            await outputFile.WriteLineAsync($"{ckTypeGraph.CkTypeId.GetName()} --|> {ckTypeGraph.BaseTypes.First(x => x.BaseTypeDepthIndex == 0).BaseCkTypeId.GetName()}");
        }
    }

    public static async void DrawAssociations(this CkTypeGraph ckTypeGraph, StreamWriter outputFile, IEnumerable<CkAssociationRoleGraph> typeAssociations)
    {
        if (ckTypeGraph.Associations.DefinedAssociations.Count != 0)
        {
            foreach (var association in ckTypeGraph.Associations.Out.Owned)
            {
                //check if Id's for associations match to create adequate multiplicities
                foreach (var item in typeAssociations)
                {
                    if (association.CkRoleId == item.CkRoleId)
                    {
                        string OutboundMultiplicityConversion = FormatOutboundMultiplicity(item);
                        string InboundMultiplicityConversion = FormatInboundMultiplicity(item);
                        await outputFile.WriteLineAsync($"{association.OriginCkTypeId.GetName()} \"{OutboundMultiplicityConversion}\" --> \"{InboundMultiplicityConversion}\" {association.TargetCkTypeId.GetName()} : {association.CkRoleId.SemanticVersionedFullName}");
                    }
                }
               
                
            }
        }
    }

    public static async void StyleClass(this CkTypeGraph ckTypeGraph, StreamWriter outputFile)
    {
        if (ckTypeGraph.IsAbstract)
        {
            await outputFile.WriteLineAsync($"style {ckTypeGraph.CkTypeId.GetName()} fill:#d4d8e9,color:##000000");
        }
        else
        {
            await outputFile.WriteLineAsync($"style {ckTypeGraph.CkTypeId.GetName()} fill:#8bc5bb,color:##000000");
        }
        
    }
    private static string FormatInboundMultiplicity(CkAssociationRoleGraph item)
    {
        var InboundMultiplicity = item.InboundMultiplicity;
        string InboundMultiplicityConversion = "n";

        if (InboundMultiplicity == Contracts.DataTransferObjects.MultiplicitiesDto.ZeroOrOne)
        {
            InboundMultiplicityConversion = "0..1";
        }
        else if (InboundMultiplicity == Contracts.DataTransferObjects.MultiplicitiesDto.One)
        {
            InboundMultiplicityConversion = "1";
        }

        return InboundMultiplicityConversion;
    }

    private static string FormatOutboundMultiplicity(CkAssociationRoleGraph item)
    {
        var OutboundMultiplicity = item.OutboundMultiplicity;
        string OutboundMultiplicityConversion = "n";

        if (OutboundMultiplicity == Contracts.DataTransferObjects.MultiplicitiesDto.ZeroOrOne)
        {
            OutboundMultiplicityConversion = "0..1";
        }
        else if (OutboundMultiplicity == Contracts.DataTransferObjects.MultiplicitiesDto.One)
        {
            OutboundMultiplicityConversion = "1";
        }

        return OutboundMultiplicityConversion;
    }
}

static class CkAttributeGraphExtensions
{
    public static async void DrawAttribute(this CkAttributeGraph ckAttributeGraph, StreamWriter outputFile)
    {
        await outputFile.WriteLineAsync($"|{ckAttributeGraph.AddAnchor()}{ckAttributeGraph.CkAttributeId.Key.SemanticVersionedFullName}| {ckAttributeGraph.ValueType} | {ckAttributeGraph.CkAttributeId.ModelId} |" +
            $"{ckAttributeGraph.DefaultValues} | {ckAttributeGraph.IsDataStream} | {ckAttributeGraph.Description} | {ckAttributeGraph.ValueCkEnumId}{ckAttributeGraph.ValueCkRecordId} |");
    }

    private static string AddAnchor(this CkAttributeGraph ckAttributeGraph)
    {
        return $"<a name=\"{ckAttributeGraph.CkAttributeId.Key.SemanticVersionedFullName}\"></a>";
    }
}


public class GenerateDocsCommand : Command<OctoToolOptions>
{
    private readonly IModelResolver _modelResolver;
    private readonly ICkYamlSerializer _ckYamlSerializer;
    private readonly IArgument _filePathArg;


    public async void GenerateMermaidTextOutput(CkModelGraph modelGraph, String classDiagramTitle, string docPath)
    {
        //StreamWriter
        using StreamWriter outputFile = new StreamWriter(Path.Combine(docPath, "diagram.md"));

        await GenerateMermaidBoilerplate(classDiagramTitle, outputFile);

        //Prints Class and Defined Attributes of Each Type if there is any
        foreach (var type in GetClasses(modelGraph))
        {
            type.DrawClass(outputFile);
            type.DrawInheritance(outputFile);
            type.DrawAssociations(outputFile, modelGraph.AssociationRoles.Select(x => x.Value));
            type.StyleClass(outputFile);
        }

        //final line to end mermaid code block
        await EndDiagram(outputFile);
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

    private IEnumerable<CkTypeGraph> GetClasses(CkModelGraph modelGraph)
    {
        return modelGraph.Types.Select(x => x.Value);
    }
    public async void GenerateMarkdownTable(CkModelGraph modelGraph, string tableTitle, string docPath, CkModelId ckModelId)
    {
        using StreamWriter outputFile = new StreamWriter(Path.Combine(docPath, "table.md"));

        await GenerateMarkdownTableBoilerplate(tableTitle, outputFile, modelGraph);


        //Add CheckmodelID function?
        if (ckModelId.ModelId == "System")
        {
            foreach (var attribute in GetAttributes(modelGraph))
            {
                if (attribute.CkAttributeId.ModelId == "System")
                {
                    attribute.DrawAttribute(outputFile);
                }

            }
        }
        else if (ckModelId.ModelId == "Basic")
        {
            foreach (var attribute in GetAttributes(modelGraph))
            {
                if (attribute.CkAttributeId.ModelId == "Basic")
                {
                    attribute.DrawAttribute(outputFile);
                }
                
            }
        }
        
    }
    private static async Task GenerateMarkdownTableBoilerplate(string tableTitle, StreamWriter outputFile, CkModelGraph ckModelGraph)
    {
        await outputFile.WriteLineAsync($"### {tableTitle}");
        await outputFile.WriteLineAsync();
        await outputFile.WriteLineAsync($"| ID      | DataType | ModelID | Default Values | Is Data Stream? | Description | CkEnumId/CkRecordId |");
        await outputFile.WriteLineAsync("| ----------- | ----------- | ----------- | ----------- | ----------- | ----------- | ----------- |");
    }

    private IEnumerable<CkAttributeGraph> GetAttributes(CkModelGraph modelGraph)
    {
        return modelGraph.Attributes.Select(x => x.Value);
    }
    public GenerateDocsCommand(ILogger<GenerateDocsCommand> logger, IModelResolver modelResolver, ICkYamlSerializer ckYamlSerializer,
        IOptions<OctoToolOptions> options)
        : base(logger, "generateDocs", "Generates docs from an compiled construction kit library", options)
    {
        _modelResolver = modelResolver;
        _ckYamlSerializer = ckYamlSerializer;

        _filePathArg = CommandArgumentValue.AddArgument("f", "file",
            ["Path of compiled construction kit model file"], true, 1);
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Creating documentation for construction kit model");

        var filePath = CommandArgumentValue.GetArgumentScalarValue<string>(_filePathArg);
        await using var stream = File.OpenRead(filePath);

        OperationResult operationResult = new(); // operation result is used to collect errors and warnings.
        var compiledModelRoot = await _ckYamlSerializer.DeserializeCompiledModelRootAsync(stream, filePath, operationResult);

        // Damit kann der aktuelle construction kit ausgewertet werden. Nicht jedoch verweise auf andere construction kit libraries.
        if (compiledModelRoot.Types != null)
        {
            foreach (var ckCompiledTypeDto in compiledModelRoot.Types)
            {
                Logger.LogInformation("{TypeId}", ckCompiledTypeDto.TypeId.ToString(CultureInfo.InvariantCulture));
            }
        }

        // Auflösen der abhängigkeiten.
        var originFileResolver = new OriginFileResolver(filePath);
        var test = await _modelResolver.ResolveAsync(compiledModelRoot, originFileResolver, operationResult);
        // Test beinhaltet nun alle aufgelösten Typen (auch von abhängigen libraries)


        //Filepath in Windows Documents Folder -.-
        string docPath = "C:\\Users\\pschw\\Desktop\\rndm stuff\\FH Salzburg\\Semester 6\\Praktikum\\Docusaurus\\construction-kit-visualizer\\src\\pages";
        
        
        GenerateMermaidTextOutput(test, "Sample CK Class Diagram", docPath);

        //0 for Basic 40 for System -> Improve in the Future
        GenerateMarkdownTable(test,"Attributes", docPath, test.Attributes.ElementAt(0).Key.ModelId);
    }
}