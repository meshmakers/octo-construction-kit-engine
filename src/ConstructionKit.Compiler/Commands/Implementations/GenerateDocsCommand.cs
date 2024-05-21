using System.Globalization;
using System.Text;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations.GenerateDocsTools;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

public class GenerateDocsCommand : Command<OctoToolOptions>
{
    private readonly IModelResolver _modelResolver;
    private readonly ICkYamlSerializer _ckYamlSerializer;
    private readonly IArgument _filePathArg;
    private readonly IArgument _docusaurusDestinationPathArg;
    private readonly IDirectoryTools _directoryTools;
    private readonly ILinkHelpers _linkHelpers;

    //Generates Full Mermaid Diagram for given CkModelGraph, ID Determines Position in File Tree
    private async Task GenerateMermaidTextOutput(CkModelGraph modelGraph, string documentPath, CkModelId ckModelId)
    {
        _directoryTools.BuildDirectory(documentPath, ckModelId);
        var baseRelativePath = _directoryTools.GetRelativeDestinationDirectory(documentPath);

        await using StreamWriter outputFile = new(_linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "index"));

        //Create Page Heading
        var split = ckModelId.SemanticVersionedFullName.Split('.');
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
            await type.LinkToType(outputFile, baseRelativePath, _linkHelpers);
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

    private static IEnumerable<CkTypeGraph> GetTypes(CkModelGraph modelGraph)
    {
        return modelGraph.Types.Select(x => x.Value);
    }

    private async Task GenerateAttributesMarkdownTable(ILogger<Command<OctoToolOptions>> logger, CkModelGraph modelGraph, string documentPath, CkModelId ckModelId)
    {
        var attributes = GetAttributes(modelGraph)
            .Where(attribute => MatchesModelId(attribute, ckModelId));
        var baseRelativePath = _directoryTools.GetRelativeDestinationDirectory(documentPath);
        
        if (attributes.Any())
        {
            _directoryTools.BuildDirectory(documentPath, ckModelId);

            await using StreamWriter outputFile = new(_linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Attributes"));
            
            await outputFile.WriteLineAsync(
                $"| {Text.ID} | {Text.DataType} | {Text.DefaultValues} | {Text.IsDataStream} |" +
                $" {Text.Description} | {Text.CkEnumId_CkRecordId} |");
            await outputFile.WriteLineAsync("| -----------| -----------| -----------| -----------| -----------| ----------- |");
            

            //Checks for If the Attributes Model ID is the Same as the one that was given
            foreach (var attribute in GetAttributes(modelGraph))
            {
                if (MatchesModelId(attribute, ckModelId))
                {
                    await attribute.DrawAttribute(outputFile, baseRelativePath, _linkHelpers);
                }
            }
        }
        else
        {
            logger.LogInformation("No Attributes to draw for model ID: {ckModelId}", ckModelId);
        }
    }

    private async Task GenerateEnumsMarkdownTable(ILogger<Command<OctoToolOptions>> logger, CkModelGraph modelGraph, string documentPath, CkModelId ckModelId)
    {
        var enums = GetEnums(modelGraph)
            .Where(en => MatchesModelId(en, ckModelId));
        if (enums.Any())
        {
            _directoryTools.BuildDirectory(documentPath, ckModelId);

            await using StreamWriter outputFile = new(_linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Enums"));


            foreach (var @enum in GetEnums(modelGraph))
            {
                if (!MatchesModelId(@enum, ckModelId)) continue;
                await AddTitle(outputFile, null, @enum.CkEnumId.Key.SemanticVersionedFullName);

                await outputFile.WriteLineAsync($"|  {Text.ID} | {Text.Values} | {Text.Descriptions} |");
                await outputFile.WriteLineAsync("| -----------| -----------| -----------|");
                    
                await @enum.DrawEnum(outputFile);
            }
        }
        else
        {
            logger.LogInformation("No Enums to draw for model ID: {ckModelId}", ckModelId);
        }
    }

    private async Task GenerateRecordsMarkdownTable(ILogger<Command<OctoToolOptions>> logger, CkModelGraph modelGraph, string documentPath, CkModelId ckModelId)
    {
        var records = GetRecords(modelGraph)
            .Where(record => MatchesModelId(record, ckModelId));

        if (records.Any())
        {
            _directoryTools.BuildDirectory(documentPath, ckModelId);

            await using StreamWriter outputFile = new(_linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Records"));
            
            await outputFile.WriteLineAsync(
                $"| {Text.ID}| {Text.DefinedAttributes} | {Text.IsOptional} | {Text.AutoIncrementReference} |" +
                $" {Text.AutoCompleteValues} | {Text.CKAttributeID} |");
            await outputFile.WriteLineAsync("| -----------| -----------| -----------| -----------| -----------| ----------- |");

            foreach (var record in GetRecords(modelGraph))
            {
                if (MatchesModelId(record, ckModelId))
                {
                    await record.DrawRecord(outputFile);
                }
            }
        }
        else
        {
            logger.LogInformation("No Records to draw for model ID: {ckModelId}", ckModelId);
        }
    }

    private async Task GenerateTypesMarkdownTable(ILogger<Command<OctoToolOptions>> logger, CkModelGraph modelGraph, 
        string documentPath, CkModelId ckModelId)
    {
        var typeGraphs = GetTypes(modelGraph)
            .Where(typeGraph => MatchesModelId(typeGraph, ckModelId));
        var baseRelativePath = _directoryTools.GetRelativeDestinationDirectory(documentPath);
        
        if (typeGraphs.Any())
        {
            _directoryTools.BuildDirectory(documentPath, ckModelId);

            await using StreamWriter outputFile = new(_linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Types"));

            foreach (var type in GetTypes(modelGraph))
            {
                if (!MatchesModelId(type, ckModelId)) continue;
                await AddTitle(outputFile, null, type.CkTypeId.Key.SemanticVersionedFullName);
                await AddHierarchy(outputFile, type, baseRelativePath);
                await AddDescription(outputFile, "SAMPLE DESCRIPTION");

                if (type.DefinedAttributes.Count != 0)
                {
                    await outputFile.WriteLineAsync(
                        $"| {Text.ID} | {Text.AutoCompleteValues} | {Text.AutoIncrementReference} | {Text.IsOptional} |");
                    await outputFile.WriteLineAsync("| -----------| -----------| -----------| ----------- |");

                    foreach (var attribute in type.DefinedAttributes)
                    {
                        await attribute.DrawAttribute(outputFile, baseRelativePath, _linkHelpers);
                    }
                }

                //For Drawing all Associations that are associated with type
                if (type.Associations.DefinedAssociations.Count == 0) continue;
                var tableBuilt = false;

                foreach (var association in type.Associations.Out.Owned)
                {
                    foreach (var item in GetAssociationRoles(modelGraph))
                    {
                        if (association.CkRoleId != item.CkRoleId) continue;
                        if (!tableBuilt)
                        {
                            await AddTitle(outputFile, null, type.CkTypeId.Key.SemanticVersionedFullName + " Associations",
                                true);

                            await outputFile.WriteLineAsync(
                                $"| {Text.ID} | {Text.InboundMultiplicity} | {Text.InboundName} |" +
                                $" {Text.OutboundMultiplicity}| {Text.OutboundName}| {Text.TargetCKTypeID}| {Text.TargetAttributes}|");
                            await outputFile.WriteLineAsync("| -----------| -----------| -----------| -----------|" +
                                                            " -----------| -----------| ----------- |");
                            tableBuilt = true;
                        }

                        await item.DrawAssociationRole(outputFile, association,
                            baseRelativePath, _linkHelpers);
                    }
                }
            }
        }
        else
        {
            logger.LogInformation("No Types to draw for model ID: {ckModelId}", ckModelId);
        }
    }

    private async Task GenerateAssociationRolesMarkdownTable(ILogger<Command<OctoToolOptions>> logger, CkModelGraph modelGraph,
        string documentPath, CkModelId ckModelId)
    {
        
        // Check if there are any association roles to draw before proceeding
        var associationRoles = GetAssociationRoles(modelGraph)
            .Where(associationRole => MatchesModelId(associationRole, ckModelId));
        var ckAssociationRoleGraphs = associationRoles as CkAssociationRoleGraph[] ?? associationRoles.ToArray();
        var baseRelativePath = _directoryTools.GetRelativeDestinationDirectory(documentPath);
        
        if (ckAssociationRoleGraphs.Length != 0)
        {
            _directoryTools.BuildDirectory(documentPath, ckModelId);
            await using StreamWriter outputFile = new(_linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Associations"));

            await outputFile.WriteLineAsync(
                $"| {Text.ID} | {Text.InboundMultiplicity} | {Text.InboundName} |" +
                $" {Text.OutboundMultiplicity}| {Text.OutboundName}| {Text.TargetCKTypeID}| {Text.TargetAttributes}|");
            await outputFile.WriteLineAsync("| -----------| -----------| -----------| -----------|" +
                                            " -----------| -----------| ----------- |");

            foreach (var associationRole in ckAssociationRoleGraphs)
            {
                await associationRole.DrawAssociationRole(outputFile, null, baseRelativePath, _linkHelpers);
            }
        }
        else
        {
            logger.LogInformation("No association roles to draw for model ID: {ckModelId}", ckModelId);
        }
    }

    private async Task GenerateVersionHistory(string docPath, CkModelId ckModelId)
    {
        _directoryTools.BuildDirectory(docPath, ckModelId);
        await using StreamWriter outputFile = new(_linkHelpers.GetGeneratedFilePath(docPath, ckModelId, "VersionHistory"));

        await outputFile.WriteLineAsync($"| {Text.Version} | {Text.Description} |");
        await outputFile.WriteLineAsync("| -----------| -----------|");
    }

    private async Task LinkToVersionHistory(StreamWriter outputFile, string baseRelativePath)
    {
        var builder = new LinkItemBuilder("VersionHistory", baseRelativePath, _linkHelpers);
        builder.BuildLinkToVersionHistory();
        await outputFile.WriteLineAsync(builder.ToString());
    }
    
    private static bool MatchesModelId(object item, CkModelId modelId)
    {
        return item switch
        {
            CkAttributeGraph attribute => attribute.CkAttributeId.ModelId.SemanticVersionedFullName == modelId.SemanticVersionedFullName,
            CkEnumGraph enumGraph => enumGraph.CkEnumId.ModelId.SemanticVersionedFullName == modelId.SemanticVersionedFullName,
            CkRecordGraph recordGraph => recordGraph.CkRecordId.ModelId.SemanticVersionedFullName == modelId.SemanticVersionedFullName,
            CkTypeGraph ckTypeGraph => ckTypeGraph.CkTypeId.ModelId.SemanticVersionedFullName == modelId.SemanticVersionedFullName,
            CkAssociationRoleGraph ckAssociationRoleGraph => ckAssociationRoleGraph.CkRoleId.ModelId.SemanticVersionedFullName ==
                                                             modelId.SemanticVersionedFullName,
            _ => false // Handle unsupported types or throw an exception if needed
        };
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

    private async Task AddHierarchy(StreamWriter outputFile, CkTypeGraph ckTypeGraph, string baseRelativePath)
    {
        var hierarchy = ReconstructHierarchyFromPath(ckTypeGraph.Path, baseRelativePath);
        await outputFile.WriteLineAsync($"**Inheritance:** {hierarchy}");
    }

    private string ReconstructHierarchyFromPath(string path, string baseRelativePath)
    {
        string[] separators = ["->", ":"];

        var parts = path.Split(separators, StringSplitOptions.TrimEntries);
        var reconstructedHierarchy = parts.Reverse();

        return BuildHierarchyString(reconstructedHierarchy.ToArray(), baseRelativePath);
    }

    private string BuildHierarchyString(string[] reconstructedHierarchy, string baseRelativePath)
    {
        StringBuilder stringBuilder = new();

        for (var i = 0; i < reconstructedHierarchy.Length; i++)
        {
            var obj = reconstructedHierarchy[i];
            var builder = new LinkItemBuilder(obj, baseRelativePath, _linkHelpers);
            builder.BuildLinkToType();
            stringBuilder.Append(builder);

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


    public GenerateDocsCommand(ILogger<GenerateDocsCommand> logger, IModelResolver modelResolver, ICkYamlSerializer ckYamlSerializer,
        IOptions<OctoToolOptions> options, IDirectoryTools directoryTools, ILinkHelpers linkHelpers)
        : base(logger, "generateDocs", "Generates docs from an compiled construction kit library", options)
    {
        _modelResolver = modelResolver;
        _ckYamlSerializer = ckYamlSerializer;
        _directoryTools = directoryTools;
        _linkHelpers = linkHelpers;

        _filePathArg = CommandArgumentValue.AddArgument("f", "file",
            ["Path of compiled construction kit model file"], true, 1);

        //use Docs folder for autogenerated Docusaurus Sidebar Functionality
        _docusaurusDestinationPathArg = CommandArgumentValue.AddArgument("o", "output", ["Path of Docusaurus Docs Directory"], true, 1);
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Creating documentation for construction kit model");

        var filePath = CommandArgumentValue.GetArgumentScalarValue<string>(_filePathArg);
        var docusaurusPath = CommandArgumentValue.GetArgumentScalarValue<string>(_docusaurusDestinationPathArg);
        
        await using var stream = File.OpenRead(filePath);

        OperationResult operationResult = new(); // operation result is used to collect errors and warnings.
        var compiledModelRoot = await _ckYamlSerializer.DeserializeCompiledModelRootAsync(stream, filePath, operationResult);

        // Evaluates current ConstructionKit
        if (compiledModelRoot.Types != null)
        {
            foreach (var ckCompiledTypeDto in compiledModelRoot.Types)
            {
                Logger.LogInformation("{TypeId}", ckCompiledTypeDto.TypeId.ToString(CultureInfo.InvariantCulture));
            }
        }

        // Resolves Dependencies
        var originFileResolver = new OriginFileResolver(filePath);
        var resolvedTypes = await _modelResolver.ResolveAsync(compiledModelRoot, originFileResolver, operationResult);

        //ID Determines Position in File Tree   
        await GenerateMermaidTextOutput(resolvedTypes, docusaurusPath, compiledModelRoot.ModelId);
        await GenerateVersionHistory(docusaurusPath, compiledModelRoot.ModelId);

        await GenerateAttributesMarkdownTable(Logger, resolvedTypes, docusaurusPath, compiledModelRoot.ModelId);
        await GenerateEnumsMarkdownTable(Logger, resolvedTypes, docusaurusPath, compiledModelRoot.ModelId);
        await GenerateRecordsMarkdownTable(Logger, resolvedTypes, docusaurusPath, compiledModelRoot.ModelId);
        await GenerateTypesMarkdownTable(Logger, resolvedTypes, docusaurusPath, compiledModelRoot.ModelId);
        await GenerateAssociationRolesMarkdownTable(Logger, resolvedTypes, docusaurusPath, compiledModelRoot.ModelId);
    }
}