using System;
using System.Globalization;
using System.Reflection.Metadata;
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
using YamlDotNet.Core.Events;
using YamlDotNet.Core.Tokens;
using static System.Net.Mime.MediaTypeNames;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

public class DocumentationContext
{
    public List<string> AttributeHeadings { get; set; } =
    [
        "ID",
        "DataType",
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

    public List<string> AssociationRolesHeadings { get; set; } =
    [
        "ID",
        "Inbound Multiplicity",
        "Inbound Name",
        "Outbound Multiplicity",
        "Outbound Name",
        "TargetCkType ID",
        "Target Attributes"
    ];
}

//Expected Format for itemName Class.Name/UnformatedAnchor
public class LinkItemBuilder(string itemName)
{
    private readonly StringBuilder _itemStringBuilder = new($"[{itemName}](");
    private readonly string _itemName = itemName;

    public void BuildLinkToType()
    {
       _itemStringBuilder.Append(LinkHelpers.CreateRelativeFilepath(_itemName.Split('/').First(), "Types"))
                          .Append('#')
                          .Append(LinkHelpers.FormatAnchor(_itemName.Split('/').Last()))
                          .Append(')');
    }

    public void BuildLinkToAttribute()
    {
        _itemStringBuilder.Append(LinkHelpers.CreateRelativeFilepath(_itemName.Split('/').First(), "Attributes"))
                           .Append('#')
                           .Append(LinkHelpers.FormatAnchor(_itemName.Split('/').Last()))
                           .Append(')');
    }

    public void BuildLinkToEnum()
    {
        _itemStringBuilder.Append(LinkHelpers.CreateRelativeFilepath(_itemName.Split('/').First(), "Enums"))
                           .Append('#')
                           .Append(LinkHelpers.FormatAnchor(_itemName.Split('/').Last()))
                           .Append(')');
    }

    public void BuildLinkToRecord()
    {
        _itemStringBuilder.Append(LinkHelpers.CreateRelativeFilepath(_itemName.Split('/').First(), "Records"))
                           .Append('#')
                           .Append(LinkHelpers.FormatAnchor(_itemName.Split('/').Last()))
                           .Append(')');
    }

    public void BuildLinkToAssociation()
    {
        _itemStringBuilder.Append(LinkHelpers.CreateRelativeFilepath(_itemName.Split('/').First(), "Associations"))
                           .Append('#')
                           .Append(LinkHelpers.FormatAnchor(_itemName.Split('/').Last()))
                           .Append(')');
    }

    public override string ToString()
    {
        return _itemStringBuilder.ToString();
    }
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

    public static async Task DrawClass(this CkTypeGraph ckTypeGraph, StreamWriter outputFile)
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

    public static async Task DrawInheritance(this CkTypeGraph ckTypeGraph, StreamWriter outputFile)
    {
        //Checks for Inheritance In BaseTypes and Creates Arrows ;)
        if (ckTypeGraph.BaseTypes.Count != 0)
        {
            await outputFile.WriteLineAsync($"{ckTypeGraph.CkTypeId.GetName()} --|> {ckTypeGraph.BaseTypes.First(x => x.BaseTypeDepthIndex == 0).BaseCkTypeId.GetName()}");
        }
    }

    public static async Task DrawAssociations(this CkTypeGraph ckTypeGraph, StreamWriter outputFile, IEnumerable<CkAssociationRoleGraph> typeAssociations)
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

    public static async Task StyleClass(this CkTypeGraph ckTypeGraph, StreamWriter outputFile)
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

    public static async Task DrawNamespaces(this CkTypeGraph ckTypeGraph, StreamWriter outputFile)
    {
        await GetNamespaceName(ckTypeGraph, outputFile);

        await outputFile.WriteLineAsync($"class {ckTypeGraph.CkTypeId.GetName()}");

        await outputFile.WriteLineAsync($"}}");
    }

    private static async Task GetNamespaceName(CkTypeGraph ckTypeGraph, StreamWriter outputFile)
    {
        await outputFile.WriteLineAsync($"namespace {ckTypeGraph.CkTypeId.ModelId.ModelId.Replace(".", "")} {{");
        //[\"{ckTypeGraph.CkTypeId.ModelId.ModelId}\"]
        //does not work as it does with classes????
    }

    public static async Task LinkToType(this CkTypeGraph ckTypeGraph, StreamWriter outputFile)
    {
        await outputFile.WriteAsync($"link {ckTypeGraph.CkTypeId.GetName()} \"");
        await outputFile.WriteAsync(LinkHelpers.CreateRelativeFilepath(ckTypeGraph.CkTypeId.ModelId.FullName, "Types"));
        await outputFile.WriteLineAsync($"#{ckTypeGraph.CreateAnchor()}\"");
    }

    public static string CreateAnchor(this CkTypeGraph ckTypeGraph)
    {
        return ckTypeGraph.CkTypeId.Key.TypeId.ToString().ToLower();
    }
}

static class CkAttributeGraphExtensions
{
    public static async Task DrawAttribute(this CkAttributeGraph ckAttributeGraph, StreamWriter outputFile, List<string> attributeHeadings)
    {
        foreach (var heading in attributeHeadings)
        {
            string content = heading switch
            {
                "ID" => $"{ckAttributeGraph.AddAnchor()}{ckAttributeGraph.AddName()}", 
                "DataType" => ckAttributeGraph.ValueType.ToString(),
                "Default Values" => ckAttributeGraph.DrawDefaultValues(),
                "Is Data Stream?" => ckAttributeGraph.IsDataStream.ToString(),
                "Description" => ckAttributeGraph.Description ?? "",
                "CkEnumId/CkRecordId" => $"{ckAttributeGraph.LinkToRecordOrEnum()}",
                _ => string.Empty
            };

 
           await outputFile.WriteAsync($"| {content} ");

        }

        await outputFile.WriteLineAsync("|"); // Finish the line for one attribute entry
    }


    private static string AddAnchor(this CkAttributeGraph ckAttributeGraph)
    {
        return $"<a id=\"{ckAttributeGraph.CkAttributeId.Key.SemanticVersionedFullName.ToLower()}\"></a>";
    }

    private static string AddName(this CkAttributeGraph ckAttributeGraph)
    {
        return $"{ckAttributeGraph.CkAttributeId.SemanticVersionedFullName}";
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

    private static string LinkToRecordOrEnum(this CkAttributeGraph ckAttributeGraph)
    {
        if(ckAttributeGraph.ValueCkEnumId != null)
        {
            var builder = new LinkItemBuilder(ckAttributeGraph.ValueCkEnumId.SemanticVersionedFullName);
            builder.BuildLinkToEnum();
            return builder.ToString();
        }
        else if(ckAttributeGraph.ValueCkRecordId != null)
        {
            var builder = new LinkItemBuilder(ckAttributeGraph.ValueCkRecordId.SemanticVersionedFullName);
            builder.BuildLinkToRecord();
            return builder.ToString();
        }
        else
        {
            return "";
        }
    }
}

static class CkEnumGraphExtensions
{
    public static async Task DrawEnum(this CkEnumGraph ckEnumGraph, StreamWriter outputFile, List<string> enumHeadings)
    {
        int counter = 0;
        foreach (var value in ckEnumGraph.Values)
        {
            foreach (var heading in enumHeadings)
            {
                string content = heading switch
                {
                    "ID" => $"{counter++}",
                    "Values" => $"{value.Name}",
                    "Descriptions" => $"{value.Description}",
                    _ => string.Empty
                };
                await outputFile.WriteAsync($"| {content} ");
            }
            await outputFile.WriteLineAsync("|");
        }
    }
}

static class CkRecordGraphExtensions
{
    public static async Task DrawRecord(this CkRecordGraph ckRecordGraph, StreamWriter outputFile, List<string> recordHeadings)
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
    public static async Task DrawAttribute(this CkTypeAttributeDto ckTypeAttributeDto, StreamWriter outputFile, List<string> attributeDtoHeadings)
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
        var builder = new LinkItemBuilder(ckTypeAttributeDto.CkAttributeId.SemanticVersionedFullName);
        builder.BuildLinkToAttribute();
        return builder.ToString();
    }
}

static class CkAssociationRoleGraphExtensions
{
    public static async Task DrawAssociationRole(this CkAssociationRoleGraph ckAssociationRoleGraph, StreamWriter outputFile, List<string> associationRoleHeadings, CkTypeAssociationGraph? association)
    {
        foreach (var heading in associationRoleHeadings)
        {
            string content = heading switch
            {
                "ID" => ckAssociationRoleGraph.AddAnchor()+ckAssociationRoleGraph.DrawLinkToDefinition(),
                "Inbound Multiplicity" => $"{ckAssociationRoleGraph.InboundMultiplicity}",
                "Inbound Name" => $"{ckAssociationRoleGraph.InboundName}",
                "Outbound Multiplicity" => $"{ckAssociationRoleGraph.OutboundMultiplicity}",
                "Outbound Name" => $"{ckAssociationRoleGraph.OutboundName}",
                "TargetCkType ID" => $"{association?.TargetCkTypeId.SemanticVersionedFullName}",
                "Target Attributes" => $"{association?.DrawTargetAttributes()}",
                _ => string.Empty
            };


            await outputFile.WriteAsync($"| {content} ");

        }

        await outputFile.WriteLineAsync("|"); // Finish the line for one attribute entry
    }

    private static string DrawLinkToDefinition(this CkAssociationRoleGraph ckAssociationRoleGraph)
    {
        //on useful 50% of the time?
        var builder = new LinkItemBuilder(ckAssociationRoleGraph.CkRoleId.SemanticVersionedFullName);
        builder.BuildLinkToAssociation();
        return builder.ToString();
    }

    private static string AddAnchor(this CkAssociationRoleGraph ckAssociationRoleGraph)
    {
        return $"<a id=\"{ckAssociationRoleGraph.CkRoleId.SemanticVersionedFullName}\"></a>";
    }

    private static string DrawTargetAttributes(this CkTypeAssociationGraph? ckTypeAssociationGraph)
    {
        if (ckTypeAssociationGraph == null)
        {
            return "";
        }
        if (ckTypeAssociationGraph.TargetAttributes != null)
        {
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
        return "";
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
        BuildDirectory(docPath, ckModelId);

        using StreamWriter outputFile = new(LinkHelpers.GetGeneratedFilePath(docPath, ckModelId, "index"));

        //Create Page Heading (could be delegated to function)
        string[] split = ckModelId.ModelId.Split('.');
        await outputFile.WriteLineAsync($"# {split.Last()}");
        await outputFile.WriteLineAsync();

        await AddDescription(outputFile, "DIAGRAM DESCRIPTION");

        await GenerateMermaidBoilerplate(ckModelId.SemanticVersionedFullName, outputFile);

        //Prints Class and Defined Attributes of Each Type if there is any
        foreach (var type in GetTypes(modelGraph))
        {
            await type.DrawClass(outputFile);
            await type.DrawInheritance(outputFile);
            await type.DrawAssociations(outputFile, modelGraph.AssociationRoles.Select(x => x.Value));
            await type.StyleClass(outputFile);
            await type.LinkToType(outputFile);
            await type.DrawNamespaces(outputFile);
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

    private static IEnumerable<CkTypeGraph> GetTypes(CkModelGraph modelGraph)
    {
        return modelGraph.Types.Select(x => x.Value);
    }
    public static async void GenerateAttributesMarkdownTable(CkModelGraph modelGraph, string docPath, CkModelId ckModelId, List<string> context)
    {
        IEnumerable<CkAttributeGraph> attributes = GetAttributes(modelGraph)
             .Where(attribute => MatchesModelId(attribute, ckModelId));
        if (attributes.Any())
        {
            BuildDirectory(docPath, ckModelId);

            using StreamWriter outputFile = new(LinkHelpers.GetGeneratedFilePath(docPath, ckModelId, "Attributes"));

            await MarkdownTableBuilder(outputFile, context);

            //Checks for If the Attributes Model ID is the Same as the one that was given
            foreach (var attribute in GetAttributes(modelGraph))
            {
                if (MatchesModelId(attribute, ckModelId))
                {
                    await attribute.DrawAttribute(outputFile, context);
                }
            }
        }
        else
        {
            Console.WriteLine($"No Attributes to draw for model ID: {ckModelId}");
        }

    }

    public static async void GenerateEnumsMarkdownTable(CkModelGraph modelGraph, string docPath, CkModelId ckModelId, List<string> context)
    {
        IEnumerable<CkEnumGraph> enums = GetEnums(modelGraph)
            .Where(en => MatchesModelId(en, ckModelId));
        if(enums.Any())
        {
            BuildDirectory(docPath, ckModelId);

            using StreamWriter outputFile = new(LinkHelpers.GetGeneratedFilePath(docPath, ckModelId, "Enums"));

            

            foreach (var Enum in GetEnums(modelGraph))
            {
                if (MatchesModelId(Enum, ckModelId))
                {
                    await AddTitle(outputFile, null, Enum.CkEnumId.Key.SemanticVersionedFullName);
                    await MarkdownTableBuilder(outputFile, context);
                    await Enum.DrawEnum(outputFile, context);
                }
            }
        }
        else
        {
            Console.WriteLine($"No Enums to draw for model ID: {ckModelId}");
        }

    }

    public static async void GenerateRecordsMarkdownTable(CkModelGraph modelGraph, string docPath, CkModelId ckModelId, List<string> context)
    {
        IEnumerable<CkRecordGraph> records = GetRecords(modelGraph)
            .Where(record => MatchesModelId(record, ckModelId));

        if (records.Any())
        {
            BuildDirectory(docPath, ckModelId);

            using StreamWriter outputFile = new(LinkHelpers.GetGeneratedFilePath(docPath, ckModelId, "Records"));

            await MarkdownTableBuilder(outputFile, context);

            foreach (var record in GetRecords(modelGraph))
            {
                if (MatchesModelId(record, ckModelId))
                {
                    await record.DrawRecord(outputFile, context);
                }
            }
        }
        else
        {
            Console.WriteLine($"No Records to draw for model ID: {ckModelId}");
        }

    }

    public static async void GenerateTypesMarkdownTable(CkModelGraph modelGraph, string docPath, CkModelId ckModelId, List<string> context)
    {
        IEnumerable<CkTypeGraph> typeGraphs = GetTypes(modelGraph)
            .Where(typeGraph => MatchesModelId(typeGraph, ckModelId));

        if (typeGraphs.Any())
        {
            BuildDirectory(docPath, ckModelId);

            using StreamWriter outputFile = new(LinkHelpers.GetGeneratedFilePath(docPath, ckModelId, "Types"));

            foreach (var type in GetTypes(modelGraph))
            {
                if (MatchesModelId(type, ckModelId))
                {
                    await AddTitle(outputFile, null, type.CkTypeId.Key.SemanticVersionedFullName);
                    await AddHierarchy(outputFile, type);
                    await AddDescription(outputFile, "SAMPLE DESCRIPTION");

                    if (type.DefinedAttributes.Count != 0)
                    {                        
                        await MarkdownTableBuilder(outputFile, context);

                        foreach (var attribute in type.DefinedAttributes)
                        {

                            await attribute.DrawAttribute(outputFile, context);
                        }                    
                    }

                    //For Drawing all Associations that are associated with type
                    if (type.Associations.DefinedAssociations.Count != 0)
                    {
                        bool tableBuilt = false;

                        foreach (var association in type.Associations.Out.Owned)
                        {
                            
                            foreach (var item in GetAssociationRoles(modelGraph))
                            {

                                var DocContextAttrib = new DocumentationContext();
                                if (association.CkRoleId == item.CkRoleId)
                                {
                                    if (!tableBuilt)
                                    {
                                        await AddTitle(outputFile, null, type.CkTypeId.Key.SemanticVersionedFullName + " Associations", true);
                                        await MarkdownTableBuilder(outputFile, DocContextAttrib.AssociationRolesHeadings);
                                        tableBuilt = true;
                                    }

                                    await item.DrawAssociationRole(outputFile, DocContextAttrib.AssociationRolesHeadings, association);
                                }
                            }


                        }
                    }
                }
            }
        }
        else
        {
            Console.WriteLine($"No Types to draw for model ID: {ckModelId}");
        }

    }

    public static async void GenerateAssociationRolesMarkdownTable(CkModelGraph modelGraph, string docPath, CkModelId ckModelId, List<string> context)
    {
        // Check if there are any association roles to draw before proceeding
        IEnumerable<CkAssociationRoleGraph> associationRoles = GetAssociationRoles(modelGraph)
            .Where(associationRole => MatchesModelId(associationRole, ckModelId));

        if (associationRoles.Any())
        {
            BuildDirectory(docPath, ckModelId);
            using StreamWriter outputFile = new(LinkHelpers.GetGeneratedFilePath(docPath, ckModelId, "Associations"));
            await MarkdownTableBuilder(outputFile, context);

            foreach (var associationRole in associationRoles)
            {
               await associationRole.DrawAssociationRole(outputFile, context, null);
            }
        }
        else
        {
            Console.WriteLine($"No association roles to draw for model ID: {ckModelId}");
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
            CkAssociationRoleGraph ckAssociationRoleGraph => ckAssociationRoleGraph.CkRoleId.ModelId.FullName == modelId.FullName,
            _ => false // Handle unsupported types or throw an exception if needed
        };
    }
    private static async Task MarkdownTableBuilder(StreamWriter outputFile, List<string> headings)
    {
        
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

    private static async Task AddTitle(StreamWriter outputFile, CkModelId? ckModelId, string tableTitle, bool isSubtitle = false)
    {
        string titlePrefix;

        if (isSubtitle)
        {
            titlePrefix = "# "; 
        }
        else if (ckModelId != null)
        {
            titlePrefix = $" {ckModelId.ModelId} ";
        }
        else
        {
            titlePrefix = " "; 
        }
        await outputFile.WriteLineAsync($"###{titlePrefix}{tableTitle}");
    }

    private static async Task AddHierarchy(StreamWriter outputFile, CkTypeGraph ckTypeGraph)
    {
        string hierarchy = ReconstructHierarchyFromPath(ckTypeGraph.Path);
        await outputFile.WriteLineAsync($"**Inheritance:** {hierarchy}");
    }

    private static string ReconstructHierarchyFromPath(string path)
    {
        string[] separators = ["->", ":"];

        var parts = path.Split(separators, StringSplitOptions.TrimEntries);
        var reconstructedhierachy = parts.Reverse();

        return BuildHierarchyString(reconstructedhierachy.ToArray());
    }

    private static string BuildHierarchyString(string[] reconstructedHierarchy)
    {
        StringBuilder stringBuilder = new();

        for (int i = 0; i < reconstructedHierarchy.Length; i++)
        {
            var obj = reconstructedHierarchy[i];
            var builder = new LinkItemBuilder(obj);
            builder.BuildLinkToType();
            stringBuilder.Append(builder.ToString());

            if (i < reconstructedHierarchy.Length - 1)
            {
                stringBuilder.Append(' ')
                             .Append('\u2794')
                             .Append(' ');
            }
        }

        return stringBuilder.ToString();
    }

    private static async Task AddDescription(StreamWriter outputFile, string description)
    {
        //if description
        await outputFile.WriteLineAsync("<details>");
        await outputFile.WriteLineAsync("<summary>Description</summary>");
        await outputFile.WriteLineAsync($"<div>{description}</div>");
        await outputFile.WriteLineAsync("</details>");
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

    private static IEnumerable<CkAssociationRoleGraph> GetAssociationRoles(CkModelGraph modelGraph)
    {
        return modelGraph.AssociationRoles.Select(x => x.Value);
    }

    private static IEnumerable<CkModelId> GetModelIDs(CkModelGraph modelGraph)
    {
        return modelGraph.Types.Select(x => x.Key.ModelId).Distinct();
    }

    private static void BuildDirectory(string docusaurusPath, CkModelId ckModelId)
    {
        string path = new(LinkHelpers.GetCommonPathParts(ckModelId));
        path = Path.Combine(docusaurusPath, path);

        try
        {
            if (Directory.Exists(path))
            {
                return;
            }

            Directory.CreateDirectory(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
        finally { }

    }

    private static CkModelId BuildIdFromFilepath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentNullException(nameof(path));
        }

        // Extract and process the model ID part from the filename
        string modelIdPart = GetModelIdPartFromPath(path);

        return new CkModelId(modelIdPart);
    }

    private static string GetModelIdPartFromPath(string path)
    {
        int lastHyphenIndex = path.LastIndexOf('-');
        if (lastHyphenIndex == -1)
        {
            throw new ArgumentException("Invalid file path format. Missing hyphen separator.");
        }

        string substringAfterLastHyphen = path[(lastHyphenIndex + 1)..];

        string[] parts = substringAfterLastHyphen.Split('.')
                                         .TakeWhile((part, index) => index < substringAfterLastHyphen.Split('.').Length - 1) // Exclude last part
                                         .Select(part => char.ToUpper(part[0]) + part[1..])
                                         .ToArray();

        return string.Join(".", parts);
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

        //Path to Docusaurus docs folder
        var docusaurusPath = CommandArgumentValue.GetArgumentScalarValue<string>(_docusaurusDestinationPathArg);

        //Generates Full Mermaid Diagram for given CkModelGraph, ID Determines Position in File Tree   
        GenerateMermaidTextOutput(test, docusaurusPath, BuildIdFromFilepath(filePath));

        var Headings = new DocumentationContext();
        var validModelIds = GetModelIDs(test);

        foreach (var modelID in validModelIds)
        {
            GenerateAttributesMarkdownTable(test, docusaurusPath, modelID, Headings.AttributeHeadings);

            GenerateEnumsMarkdownTable(test, docusaurusPath, modelID, Headings.EnumHeadings);

            GenerateRecordsMarkdownTable(test, docusaurusPath, modelID, Headings.RecordHeadings);

            GenerateTypesMarkdownTable(test, docusaurusPath, modelID, Headings.AttributeDtoHeadings);

            GenerateAssociationRolesMarkdownTable(test, docusaurusPath, modelID, Headings.AssociationRolesHeadings);
        }

        
     }
}