using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

public class DocumentationContext
{
    public List<string> AttributeHeadings { get; set; } =
    [
        "ID",
        "DataType",
        "ModelID",
        "Default Values",
        "Is Data Stream?",
        "Description",
        "CkEnumId/CkRecordId"
    ];

    public List<string> EnumHeadings { get; set; } =
    [
        "ID",
        "Values",
        "Descriptions"
    ];

    public List<string> RecordHeadings { get; set; } =
    [
        "ID",
        "Defined Attributes",
        "Is Optional",
        "Auto Increment Reference",
        "Auto Complete Values",
        "CKAttributeID"
    ];

    public List<string> AttributeDtoHeadings { get; set; } =
    [
        "ID",
        "Auto Complete Values",
        "Auto Increment Reference",
        "Is Optional"
        //ModelID ?
    ];
}
static class CkTypeGraphExtensions
{
    public static string GetClassName(this CkId<CkTypeId> ckTypeGraph)
    {
        string fullName = ckTypeGraph.Key.SemanticVersionedFullName;

        string sanitizedFullName = GetName(ckTypeGraph) + "[\"" + fullName + "\"]";

        return sanitizedFullName;
    }
    public static string GetName(this CkId<CkTypeId> ckTypeGraph)
    {
        string fullName = ckTypeGraph.Key.SemanticVersionedFullName;

        string sanitizedFullName = fullName.Replace(".", "");

        return sanitizedFullName;
    }

    public static async void DrawClass(this CkTypeGraph ckTypeGraph, StreamWriter outputFile)
    {
        await outputFile.WriteAsync($"class {ckTypeGraph.CkTypeId.GetClassName()}");

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
    public static async void DrawAttribute(this CkAttributeGraph ckAttributeGraph, StreamWriter outputFile, List<string> attributeHeadings)
    {
        foreach (var heading in attributeHeadings)
        {
            string content = heading switch
            {
                "ID" => $"{ckAttributeGraph.AddAnchor()}{ckAttributeGraph.AddLink()}", 
                "DataType" => ckAttributeGraph.ValueType.ToString(),
                "ModelID" => ckAttributeGraph.CkAttributeId.ModelId.ToString(),
                "Default Values" => ckAttributeGraph.DrawDefaultValues(),
                "Is Data Stream?" => ckAttributeGraph.IsDataStream.ToString(),
                "Description" => ckAttributeGraph.Description ?? "",
                "CkEnumId/CkRecordId" => $"{ckAttributeGraph.ValueCkEnumId}{ckAttributeGraph.ValueCkRecordId}",
                _ => string.Empty
            };

 
           await outputFile.WriteAsync($"| {content} ");

        }

        await outputFile.WriteLineAsync("|"); // Finish the line for one attribute entry
    }


    private static string AddAnchor(this CkAttributeGraph ckAttributeGraph)
    {
        return $"<a id=\"{ckAttributeGraph.CkAttributeId.Key.SemanticVersionedFullName}\"></a>";
    }

    private static string AddLink(this CkAttributeGraph ckAttributeGraph)
    {
        return $"[{ckAttributeGraph.CkAttributeId.Key.SemanticVersionedFullName}](/diagram)";
    }

    private static string DrawDefaultValues(this CkAttributeGraph ckAttributeGraph)
    {   
        StringBuilder stringBuilder = new();
        if (ckAttributeGraph.DefaultValues != null)
        {
            foreach (var value in ckAttributeGraph.DefaultValues)
            {
                stringBuilder.Append(value.ToString());
            }
            return stringBuilder.ToString();
        }
        return "";
        
    }
}

static class CkEnumGraphExtensions
{
    public static async void DrawEnum(this CkEnumGraph ckEnumGraph, StreamWriter outputFile, List<string> enumHeadings)
    {
        foreach (var heading in enumHeadings)
        {
            string content = heading switch
            {
                "ID" => $"{ckEnumGraph.AddAnchor()}{ckEnumGraph.CkEnumId.SemanticVersionedFullName}",
                "Values" => ckEnumGraph.DrawValuesOrDescriptions(true),
                "Descriptions" => ckEnumGraph.DrawValuesOrDescriptions(false),
                _ => string.Empty
            };


            await outputFile.WriteAsync($"| {content} ");

        }

        await outputFile.WriteLineAsync("|"); // Finish the line for one attribute entry
    }

    private static string DrawValuesOrDescriptions(this CkEnumGraph ckEnumGraph, bool drawValues)
    {
        StringBuilder stringBuilder = new();
        stringBuilder.Append("<ol start=\"0\">");
        foreach (var value in ckEnumGraph.Values)
        {
            stringBuilder.Append("<li>");
            stringBuilder.Append(drawValues ? $"{value.Name}" : $"{value.Description}");
            stringBuilder.Append("</li>");
        }
        stringBuilder.Append("</ol>");
        
        return stringBuilder.ToString();
    }

    private static string AddAnchor(this CkEnumGraph ckEnumGraph)
    {
        return $"<a id=\"{ckEnumGraph.CkEnumId.SemanticVersionedFullName}\"></a>";
    }
}

static class CkRecordGraphExtensions
{
    public static async void DrawRecord(this CkRecordGraph ckRecordGraph, StreamWriter outputFile, List<string> recordHeadings)
    {
        foreach (var heading in recordHeadings)
        {
            string content = heading switch
            {
                "ID" => $"{ckRecordGraph.CkRecordId.SemanticVersionedFullName}",
                "Defined Attributes" => ckRecordGraph.DrawAttributeList((a) => a.AttributeName),
                "Is Optional" => ckRecordGraph.DrawAttributeList((a) => a.IsOptional.ToString()),
                "Auto Increment Reference" => ckRecordGraph.DrawAttributeAutoIncrementReference(),
                "Auto Complete Values" => ckRecordGraph.DrawAttributeAutoCompleteValues(),
                "CKAttributeID" => ckRecordGraph.DrawAttributeList((a) => a.CkAttributeId.SemanticVersionedFullName),
                _ => string.Empty
            };


            await outputFile.WriteAsync($"| {content} ");

        }

        await outputFile.WriteLineAsync("|"); // Finish the line for one attribute entry
    }

    private static string DrawAttributeList(this CkRecordGraph ckRecordGraph, Func<CkTypeAttributeDto, string> valueGetter)
    {
        StringBuilder stringBuilder = new();
        stringBuilder.Append("<ul style={{ listStyleType: \"none\" }}>");
        foreach (var attribute in ckRecordGraph.DefinedAttributes)
        {
            stringBuilder.Append("<li>");
            stringBuilder.Append(valueGetter(attribute));
            stringBuilder.Append("</li>");
        }
        stringBuilder.Append("</ul>");

        return stringBuilder.ToString();
    }

    
    private static string DrawAttributeAutoIncrementReference(this CkRecordGraph ckRecordGraph)
    {
        StringBuilder stringBuilder = new();
        stringBuilder.Append("<ul style={{ listStyleType: \"none\" }}>");
        foreach (var attribute in ckRecordGraph.DefinedAttributes)
        {
            stringBuilder.Append("<li>");
            stringBuilder.Append($"{attribute.AutoIncrementReference}");
            stringBuilder.Append("</li>");
        }
        stringBuilder.Append("</ul>");

        return stringBuilder.ToString();
    }

    //Refactor for the love of God
    private static string DrawAttributeAutoCompleteValues(this CkRecordGraph ckRecordGraph)
    {
        StringBuilder stringBuilder = new();
        stringBuilder.Append("<ul style={{ listStyleType: \"none\" }}>");
        foreach (var attribute in ckRecordGraph.DefinedAttributes)
        {
            if (attribute.AutoCompleteValues != null)
            {
                stringBuilder.Append("<li>");
                stringBuilder.Append("<ul style={{ listStyleType: \"none\" }}>");
                foreach (var autocompletevalue in attribute.AutoCompleteValues)
                {
                    stringBuilder.Append("<li>");
                    stringBuilder.Append($"{autocompletevalue}");
                    stringBuilder.Append("</li>");
                }
                stringBuilder.Append("</ul>");
                stringBuilder.Append("</li>");
            }   
        }
        stringBuilder.Append("</ul>");

        return stringBuilder.ToString();
    }
}

static class CkTypeAttributeDtoExtensions
{
    public static async void DrawAttribute(this CkTypeAttributeDto ckTypeAttributeDto, StreamWriter outputFile, List<string> attributeDtoHeadings)
    {
        foreach (var heading in attributeDtoHeadings)
        {
            string content = heading switch
            {
                "ID" => ckTypeAttributeDto.DrawLinkToDefinition(),
                "Auto Complete Values" => ckTypeAttributeDto.DrawAttributeAutoCompleteValues(),
                "Auto Increment Reference" => ckTypeAttributeDto.DrawAttributeAutoIncrementReference(),
                "Is Optional" => ckTypeAttributeDto.IsOptional.ToString(),
                _ => string.Empty
            };


            await outputFile.WriteAsync($"| {content} ");
        }

        await outputFile.WriteLineAsync("|"); // Finish the line for one attribute entry
    }

    private static string DrawAttributeAutoCompleteValues(this CkTypeAttributeDto ckTypeAttributeDto)
    {
        if (ckTypeAttributeDto.AutoCompleteValues != null)
        {
            StringBuilder stringBuilder = new();
            stringBuilder.Append("<ul style={{ listStyleType: \"none\" }}>");


            foreach (var attribute in ckTypeAttributeDto.AutoCompleteValues)
            {
                stringBuilder.Append("<li>");
                stringBuilder.Append($"{attribute}");
                stringBuilder.Append("</li>");
            }

            stringBuilder.Append("</ul>");
            return stringBuilder.ToString();
        }
        return "";
    }

    private static string DrawAttributeAutoIncrementReference(this CkTypeAttributeDto ckTypeAttributeDto)
    {
        if (ckTypeAttributeDto.AutoIncrementReference != null)
        {
            StringBuilder stringBuilder = new();
            stringBuilder.Append("<ul style={{ listStyleType: \"none\" }}>");
            foreach (var attribute in ckTypeAttributeDto.AutoIncrementReference)
            {
                stringBuilder.Append("<li>");
                stringBuilder.Append($"{attribute}");
                stringBuilder.Append("</li>");
            }
            stringBuilder.Append("</ul>");

            return stringBuilder.ToString();
        }

        return "";
    }

    private static string DrawLinkToDefinition(this CkTypeAttributeDto ckTypeAttributeDto)
    {
        string link = new(LinkHelpers.CreateRelativeFilepath(ckTypeAttributeDto.CkAttributeId.ModelId, "Attributes"));
        link = "[" + ckTypeAttributeDto.AttributeName + "]" + "(" + link + ")";    
        return link;
    }
}

public class GenerateDocsCommand : Command<OctoToolOptions>
{
    private readonly IModelResolver _modelResolver;
    private readonly ICkYamlSerializer _ckYamlSerializer;
    private readonly IArgument _filePathArg;
    private readonly IArgument _docusaurusDestinationPathArg;

    public static async void GenerateMermaidTextOutput(CkModelGraph modelGraph, string docPath, CkModelId ckModelId)
    {
        using StreamWriter outputFile = new(LinkHelpers.GetGeneratedFilePath(docPath, ckModelId, "Diagram"));

        await GenerateMermaidBoilerplate(ckModelId.SemanticVersionedFullName, outputFile);

        //Prints Class and Defined Attributes of Each Type if there is any
        foreach (var type in GetClasses(modelGraph))
        {
            type.DrawClass(outputFile);
            type.DrawInheritance(outputFile);
            type.DrawAssociations(outputFile, modelGraph.AssociationRoles.Select(x => x.Value));
            type.StyleClass(outputFile);
            type.LinkToType(outputFile);
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

    private static IEnumerable<CkTypeGraph> GetClasses(CkModelGraph modelGraph)
    {
        return modelGraph.Types.Select(x => x.Value);
    }
    public static async void GenerateAttributesMarkdownTable(CkModelGraph modelGraph, string docPath, CkModelId ckModelId, List<string> context)
    {
        using StreamWriter outputFile = new(LinkHelpers.GetGeneratedFilePath(docPath, ckModelId, "Attributes"));

        await MarkdownTableBuilder(outputFile, ckModelId, "Attributes", context);

        //Checks for If the Attributes Model ID is the Same as the one that was given
        foreach (var attribute in GetAttributes(modelGraph))
        {
            if (MatchesModelId(attribute, ckModelId))
            {
                attribute.DrawAttribute(outputFile, context);
            }
        }

    }

    public static async void GenerateEnumsMarkdownTable(CkModelGraph modelGraph, string docPath, CkModelId ckModelId, List<string> context)
    {
        using StreamWriter outputFile = new(LinkHelpers.GetGeneratedFilePath(docPath, ckModelId, "Enums"));

        await MarkdownTableBuilder(outputFile, ckModelId, "Enums", context);

        foreach (var Enum in GetEnums(modelGraph))
        {
            if (MatchesModelId(Enum, ckModelId))
            {
                Enum.DrawEnum(outputFile, context);
            } 
        }
    }

    public static async void GenerateRecordsMarkdownTable(CkModelGraph modelGraph, string docPath, CkModelId ckModelId, List<string> context)
    {
        using StreamWriter outputFile = new(LinkHelpers.GetGeneratedFilePath(docPath, ckModelId, "Records"));

        await MarkdownTableBuilder(outputFile, ckModelId, "Records", context);

        foreach (var record in GetRecords(modelGraph))
        {
            if (MatchesModelId(record, ckModelId))
            {
                record.DrawRecord(outputFile, context);
            }
        }
    }

    public static async void GenerateTypesMarkdownTable(CkModelGraph modelGraph, string docPath, CkModelId ckModelId, List<string> context)
    {
        using StreamWriter outputFile = new(LinkHelpers.GetGeneratedFilePath(docPath, ckModelId, "Types"));

        foreach (var type in GetClasses(modelGraph))
        {
            if (MatchesModelId(type, ckModelId))
            {
                if (type.DefinedAttributes.Count == 0)
                {
                    await outputFile.WriteLineAsync($"### {type.CkTypeId.ModelId.FullName} {type.CkTypeId.Key.SemanticVersionedFullName}");
                }
                else
                {
                    await MarkdownTableBuilder(outputFile, type.CkTypeId.ModelId, type.CkTypeId.Key.SemanticVersionedFullName, context);

                    foreach (var attribute in type.DefinedAttributes)
                    {

                        attribute.DrawAttribute(outputFile, context);
                    }
                }
            }
        }
    }

    //C# Pattern Matching insanity
    private static bool MatchesModelId(object item, CkModelId modelId)
    {
        return item switch
        {
            CkAttributeGraph attribute => attribute.CkAttributeId.ModelId.FullName == modelId.FullName,
            CkEnumGraph enumGraph => enumGraph.CkEnumId.ModelId.FullName == modelId.FullName,
            CkRecordGraph recordGraph => recordGraph.CkRecordId.ModelId.FullName == modelId.FullName,
            CkTypeGraph ckTypeGraph => ckTypeGraph.CkTypeId.ModelId.FullName == modelId.FullName,
            _ => false // Handle unsupported types or throw an exception if needed
        };
    }
    private static async Task MarkdownTableBuilder(StreamWriter outputFile, CkModelId ckModelId, string tableTitle, List<string> headings)
    {
        await outputFile.WriteLineAsync($"### {ckModelId.FullName} {tableTitle}");
        await outputFile.WriteLineAsync();
        foreach (var i in headings)
        {
            await outputFile.WriteAsync($"| {i}");
        }
        await outputFile.WriteLineAsync(" |");
        foreach (var _ in headings)
        {
            await outputFile.WriteAsync($"| -----------");
        }
        await outputFile.WriteLineAsync(" |");
    }

    private static IEnumerable<CkAttributeGraph> GetAttributes(CkModelGraph modelGraph)
    {
        return modelGraph.Attributes.Select(x => x.Value);
    }

    private static IEnumerable<CkEnumGraph> GetEnums(CkModelGraph modelGraph)
    {
        return modelGraph.Enums.Select(x => x.Value);
    }

    private static IEnumerable<CkRecordGraph> GetRecords(CkModelGraph modelGraph)
    {
        return modelGraph.Records.Select(x => x.Value);
    }

    private static void BuildDirectoryStructure(string docusaurusPath)
    {
        string path = Path.Combine(docusaurusPath, "System");
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

    public GenerateDocsCommand(ILogger<GenerateDocsCommand> logger, IModelResolver modelResolver, ICkYamlSerializer ckYamlSerializer,
        IOptions<OctoToolOptions> options)
        : base(logger, "generateDocs", "Generates docs from an compiled construction kit library", options)
    {
        _modelResolver = modelResolver;
        _ckYamlSerializer = ckYamlSerializer;

        _filePathArg = CommandArgumentValue.AddArgument("f", "file",
            ["Path of compiled construction kit model file"], true, 1);

        //use Docs folder for autogenerated Docusaurus Sidebar Functionality
        _docusaurusDestinationPathArg = CommandArgumentValue.AddArgument("d", "destination", ["Path of Docusaurus Docs Directory"], true, 1);
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

        
        CkModelId ckModelIdSystem = new("System", "1.0.0");
        CkModelId ckModelIdBasic = new("Basic", "1.0.0");
        CkModelId ckModelIdIndustryBasic = new("IndustryBasic", "1.0.0");
        CkModelId ckModelIdIndustryEnergy = new("IndustryEnergy", "1.0.0");
        CkModelId ckModelIdIndustryFluid = new("IndustryFluid", "1.0.0");

        CkModelId[] ckModelIds = [ckModelIdSystem, ckModelIdBasic , ckModelIdIndustryBasic, ckModelIdIndustryEnergy, ckModelIdIndustryFluid];


        //Step 1
        BuildDirectoryStructure(docusaurusPath);

        //Generates Full Mermaid Diagram for given CkModelGraph, ID Determines Position in File Tree
        GenerateMermaidTextOutput(test, docusaurusPath, ckModelIdIndustryFluid);

        var Headings = new DocumentationContext();
        

        foreach (var modelID in ckModelIds)
        {
            GenerateAttributesMarkdownTable(test, docusaurusPath, modelID, Headings.AttributeHeadings);

            GenerateEnumsMarkdownTable(test, docusaurusPath, modelID, Headings.EnumHeadings);

            GenerateRecordsMarkdownTable(test, docusaurusPath, modelID, Headings.RecordHeadings);

            GenerateTypesMarkdownTable(test, docusaurusPath, modelID, Headings.AttributeDtoHeadings);
        }

        
    }
}