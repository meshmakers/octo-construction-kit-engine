using System.Globalization;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;
using YamlDotNet.Core.Tokens;

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
        await outputFile.WriteLineAsync($"|{ckAttributeGraph.AddAnchor()}{ckAttributeGraph.AddLink()}| {ckAttributeGraph.ValueType} | {ckAttributeGraph.CkAttributeId.ModelId} |" +
            $"{ckAttributeGraph.DefaultValues} | {ckAttributeGraph.IsDataStream} | {ckAttributeGraph.Description} | {ckAttributeGraph.ValueCkEnumId}{ckAttributeGraph.ValueCkRecordId} |");
    }

    private static string AddAnchor(this CkAttributeGraph ckAttributeGraph)
    {
        return $"<a id=\"{ckAttributeGraph.CkAttributeId.Key.SemanticVersionedFullName}\"></a>";
    }

    private static string AddLink(this CkAttributeGraph ckAttributeGraph)
    {
        return $"[{ckAttributeGraph.CkAttributeId.Key.SemanticVersionedFullName}](/diagram)";
    }
}

static class CkEnumGraphExtensions
{
    public static async void DrawEnum(this CkEnumGraph ckEnumGraph, StreamWriter outputFile)
    {
        await outputFile.WriteAsync($"| {ckEnumGraph.AddAnchor()}{ckEnumGraph.CkEnumId.SemanticVersionedFullName} |");
        ckEnumGraph.DrawValuesOrDescriptions(outputFile, true);
        ckEnumGraph.DrawValuesOrDescriptions(outputFile, false);
        await outputFile.WriteLineAsync();
    }

    private static async void DrawValuesOrDescriptions(this CkEnumGraph ckEnumGraph, StreamWriter outputFile, bool drawValues)
    {
        await outputFile.WriteAsync("<ol start=\"0\">");
        foreach (var value in ckEnumGraph.Values)
        {
            await outputFile.WriteAsync("<li>");
            await outputFile.WriteAsync(drawValues ? $"{value.Name}" : $"{value.Description}");
            await outputFile.WriteAsync("</li>");
        }
        await outputFile.WriteAsync("</ol>");
        await outputFile.WriteAsync(" |");
    }

    private static string AddAnchor(this CkEnumGraph ckEnumGraph)
    {
        return $"<a id=\"{ckEnumGraph.CkEnumId.SemanticVersionedFullName}\"></a>";
    }
}

static class CkRecordGraphExtensions
{
    public static async void DrawRecord(this CkRecordGraph ckRecordGraph, StreamWriter outputFile)
    {
        await outputFile.WriteAsync($"|{ckRecordGraph.CkRecordId.SemanticVersionedFullName} |");

        ckRecordGraph.DrawAttributeList(outputFile, (a) => a.AttributeName);
        ckRecordGraph.DrawAttributeList(outputFile, (a) => a.IsOptional.ToString());
        ckRecordGraph.DrawAttributeAutoIncrementReferenceOrAutoCompleteValues(outputFile, false);
        ckRecordGraph.DrawAttributeAutoIncrementReferenceOrAutoCompleteValues(outputFile, true);

        ckRecordGraph.DrawAttributeList(outputFile, (a) => a.CkAttributeId.SemanticVersionedFullName);
        await outputFile.WriteLineAsync();
    }

    private static async void DrawAttributeList(this CkRecordGraph ckRecordGraph, StreamWriter outputFile, Func<CkTypeAttributeDto, string> valueGetter)
    {
        await outputFile.WriteAsync("<ul style={{ listStyleType: \"none\" }}>");
        foreach (var attribute in ckRecordGraph.DefinedAttributes)
        {
            await outputFile.WriteAsync("<li>");
            await outputFile.WriteAsync(valueGetter(attribute));
            await outputFile.WriteAsync("</li>");
        }
        await outputFile.WriteAsync("</ul>");
        await outputFile.WriteAsync(" |");
    }

    //!! Potentially Change especially considering AutoComplete Values May have more than 1 element
    private static async void DrawAttributeAutoIncrementReferenceOrAutoCompleteValues(this CkRecordGraph ckRecordGraph, StreamWriter outputFile, bool drawAutoIncrementReference)
    {
        await outputFile.WriteAsync("<ul style={{ listStyleType: \"none\" }}>");
        foreach (var attribute in ckRecordGraph.DefinedAttributes)
        {
            await outputFile.WriteAsync("<li>");
            await outputFile.WriteAsync(drawAutoIncrementReference ? $"{attribute.AutoIncrementReference}" : $"{attribute.AutoCompleteValues}");
            await outputFile.WriteAsync("</li>");
        }
        await outputFile.WriteAsync("</ul>");
        await outputFile.WriteAsync(" |");
    }
}

public class GenerateDocsCommand : Command<OctoToolOptions>
{
    private readonly IModelResolver _modelResolver;
    private readonly ICkYamlSerializer _ckYamlSerializer;
    private readonly IArgument _filePathArg;
    private readonly IArgument _docusaurusDestinationPathArg;

    public async void GenerateMermaidTextOutput(CkModelGraph modelGraph, String classDiagramTitle, string docPath, CkModelId ckModelId)
    {
        //StreamWriter
        //using StreamWriter outputFile = new(Path.Combine(docPath, "diagram.md"));

        using StreamWriter outputFile = new(Path.Combine(BuildFilepath(docPath, ckModelId), ckModelId.SemanticVersionedFullName + "-Diagram.md"));

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
    public async void GenerateAttributesMarkdownTable(CkModelGraph modelGraph, string tableTitle, string docPath, CkModelId ckModelId, string[] headings)
    {
        //using StreamWriter outputFile = new(Path.Combine(docPath, docName));

        using StreamWriter outputFile = new(Path.Combine(BuildFilepath(docPath, ckModelId), ckModelId.SemanticVersionedFullName + "-Attributes.md"));

        await MarkdownTableBuilder(outputFile, ckModelId, tableTitle, headings);

        //Checks for If the Attributes Model ID is the Same as the one that was given
        foreach (var attribute in GetAttributes(modelGraph))
        {
            if (attribute.CkAttributeId.ModelId.FullName == ckModelId.FullName)
            {
                attribute.DrawAttribute(outputFile);
            }
        }

    }

    public async void GenerateEnumsMarkdownTable(CkModelGraph modelGraph, string tableTitle, string docPath, CkModelId ckModelId, string[] headings)
    {
        //using StreamWriter outputFile = new(Path.Combine(docPath, docName));

        using StreamWriter outputFile = new(Path.Combine(BuildFilepath(docPath, ckModelId), ckModelId.SemanticVersionedFullName + "-Enums.md"));

        await MarkdownTableBuilder(outputFile, ckModelId, tableTitle, headings);

        foreach (var Enum in GetEnums(modelGraph))
        {
            if (Enum.CkEnumId.ModelId.FullName == ckModelId.FullName)
            {
                Enum.DrawEnum(outputFile);
            } 
        }
    }

    public async void GenerateRecordsMarkdownTable(CkModelGraph modelGraph, string tableTitle, string docPath, CkModelId ckModelId, string[] headings)
    {
        //using StreamWriter outputFile = new(Path.Combine(docPath, docName));

        using StreamWriter outputFile = new(Path.Combine(BuildFilepath(docPath, ckModelId), ckModelId.SemanticVersionedFullName + "-Records.md"));

        await MarkdownTableBuilder(outputFile, ckModelId, tableTitle, headings);

        foreach (var record in GetRecords(modelGraph))
        {
            if (record.CkRecordId.ModelId.FullName == ckModelId.FullName)
            {
                record.DrawRecord(outputFile);
            }
        }
    }

    //Potentially Rework TableTitle Since IT should always be Clear considering which function calls this function
    private static async Task MarkdownTableBuilder(StreamWriter outputFile, CkModelId ckModelId, string tableTitle, string[] headings)
    {
        await outputFile.WriteLineAsync($"### {ckModelId.FullName} {tableTitle}");
        await outputFile.WriteLineAsync();
        foreach (var i in headings)
        {
            await outputFile.WriteAsync($"| {i}");
        }
        await outputFile.WriteLineAsync(" |");
        foreach (var i in headings)
        {
            await outputFile.WriteAsync($"| -----------");
        }
        await outputFile.WriteLineAsync(" |");
    }

    private IEnumerable<CkAttributeGraph> GetAttributes(CkModelGraph modelGraph)
    {
        return modelGraph.Attributes.Select(x => x.Value);
    }

    private IEnumerable<CkEnumGraph> GetEnums(CkModelGraph modelGraph)
    {
        return modelGraph.Enums.Select(x => x.Value);
    }

    private IEnumerable<CkRecordGraph> GetRecords(CkModelGraph modelGraph)
    {
        return modelGraph.Records.Select(x => x.Value);
    }

    private static void BuildDirectoryStructure(string docusaurusPagesPath)
    {
        string path = Path.Combine(docusaurusPagesPath, "System");
       try
       {
            if (Directory.Exists(path))
            {
                Console.WriteLine("Path Exists");
                return;
            }

            DirectoryInfo di = Directory.CreateDirectory(path);
            
            di = Directory.CreateDirectory(Path.Combine(path, "Basic"));

            di = Directory.CreateDirectory(Path.Combine(path, "Basic", "Industry"));

            di = Directory.CreateDirectory(Path.Combine(path, "Basic", "Industry", "Energy"));

            di = Directory.CreateDirectory(Path.Combine(path, "Basic", "Industry", "Fluid"));

        }
        catch(Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
        finally { }
    }

    public static string BuildFilepath(string docusaurusPagesPath, CkModelId ckModelId)
    {
        string path = "System";
        path = Path.Combine(docusaurusPagesPath, path);

        if (ckModelId.ModelId.Contains("Basic"))
        {
            path = Path.Combine(path, "Basic");
        }

        return path;
    }
    public GenerateDocsCommand(ILogger<GenerateDocsCommand> logger, IModelResolver modelResolver, ICkYamlSerializer ckYamlSerializer,
        IOptions<OctoToolOptions> options)
        : base(logger, "generateDocs", "Generates docs from an compiled construction kit library", options)
    {
        _modelResolver = modelResolver;
        _ckYamlSerializer = ckYamlSerializer;

        _filePathArg = CommandArgumentValue.AddArgument("f", "file",
            ["Path of compiled construction kit model file"], true, 1);

        _docusaurusDestinationPathArg = CommandArgumentValue.AddArgument("d", "destination", ["Path of Docusaurus Pages Directory"], true, 1);
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


        //Old Static Filepath
        //string docPath = "C:\\Users\\pschw\\Desktop\\rndm stuff\\FH Salzburg\\Semester 6\\Praktikum\\Docusaurus\\construction-kit-visualizer\\src\\pages";
        var docusaurusPath = CommandArgumentValue.GetArgumentScalarValue<string>(_docusaurusDestinationPathArg);

        string[] attributeHeadings = ["ID", "DataType", "ModelID", "Default Values", "Is Data Stream?", "Description", "CkEnumId/CkRecordId"];
        string[] enumHeadings = ["ID", "Values", "Descriptions"];
        string[] recordHeadings = ["ID", "Defined Attributes", "Is Optional", "Auto Complete Values", "Auto Increment Reference", "CKAttributeID"];

        CkModelId ckModelIdSystem = new("System", "1.0.0");
        CkModelId ckModelIdBasic = new("Basic", "1.0.0");

        BuildDirectoryStructure(docusaurusPath);

        GenerateMermaidTextOutput(test, "Sample CK Class Diagram", docusaurusPath, ckModelIdBasic);

        GenerateAttributesMarkdownTable(test,"Attributes", docusaurusPath, ckModelIdBasic, attributeHeadings);

        GenerateAttributesMarkdownTable(test, "Attributes", docusaurusPath, ckModelIdSystem, attributeHeadings);

        GenerateEnumsMarkdownTable(test, "Enums", docusaurusPath, ckModelIdBasic, enumHeadings);

        GenerateRecordsMarkdownTable(test, "Records", docusaurusPath, ckModelIdBasic, recordHeadings);
    }
}