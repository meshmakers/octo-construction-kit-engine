using System.Text;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations.GenerateDocsTools;

public class ContentGenerator(IDirectoryTools directoryTools, ILinkHelpers linkHelpers) : IContentGenerator
{

    public async Task GenerateAttributesMarkdownTable(ILogger<Command<OctoToolOptions>> logger, CkModelGraph modelGraph, string 
        documentPath, CkModelId ckModelId)
    {
        var attributes = GetValues.GetAttributes(modelGraph)
            .Where(attribute => MatchesModelId(attribute, ckModelId));
        var baseRelativePath = directoryTools.GetRelativeDestinationDirectory(documentPath);
        
        if (attributes.Any())
        {
            directoryTools.BuildDirectory(documentPath, ckModelId);

            await using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Attributes"));
            
            await outputFile.WriteLineAsync(
                $"| {Text.ID} | {Text.DataType} | {Text.DefaultValues} | {Text.IsDataStream} |" +
                $" {Text.Description} | {Text.CkEnumId_CkRecordId} |");
            await outputFile.WriteLineAsync("| -----------| -----------| -----------| -----------| -----------| ----------- |");
            

            //Checks for If the Attributes Model ID is the Same as the one that was given
            foreach (var attribute in GetValues.GetAttributes(modelGraph))
            {
                if (MatchesModelId(attribute, ckModelId))
                {
                    await attribute.DrawAttribute(outputFile, baseRelativePath, linkHelpers);
                }
            }
        }
        else
        {
            logger.LogInformation("No Attributes to draw for model ID: {ckModelId}", ckModelId);
        }
    }

    public async Task GenerateEnumsMarkdownTable(ILogger<Command<OctoToolOptions>> logger, CkModelGraph modelGraph, string documentPath, 
        CkModelId ckModelId)
    {
        var enums = GetValues.GetEnums(modelGraph)
            .Where(en => MatchesModelId(en, ckModelId));
        if (enums.Any())
        {
            directoryTools.BuildDirectory(documentPath, ckModelId);

            await using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Enums"));


            foreach (var @enum in GetValues.GetEnums(modelGraph))
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

    public async Task GenerateRecordsMarkdownTable(ILogger<Command<OctoToolOptions>> logger, CkModelGraph modelGraph, string documentPath, 
        CkModelId ckModelId)
    {
        var records = GetValues.GetRecords(modelGraph)
            .Where(record => MatchesModelId(record, ckModelId));

        if (records.Any())
        {
            directoryTools.BuildDirectory(documentPath, ckModelId);

            await using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Records"));
            
            await outputFile.WriteLineAsync(
                $"| {Text.ID}| {Text.DefinedAttributes} | {Text.IsOptional} | {Text.AutoIncrementReference} |" +
                $" {Text.AutoCompleteValues} | {Text.CKAttributeID} |");
            await outputFile.WriteLineAsync("| -----------| -----------| -----------| -----------| -----------| ----------- |");

            foreach (var record in GetValues.GetRecords(modelGraph))
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

    public async Task GenerateTypesMarkdownTable(ILogger<Command<OctoToolOptions>> logger, CkModelGraph modelGraph, 
        string documentPath, CkModelId ckModelId)
    {
        var typeGraphs = GetValues.GetTypes(modelGraph)
            .Where(typeGraph => MatchesModelId(typeGraph, ckModelId));
        var baseRelativePath = directoryTools.GetRelativeDestinationDirectory(documentPath);
        
        if (typeGraphs.Any())
        {
            directoryTools.BuildDirectory(documentPath, ckModelId);

            await using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Types"));

            foreach (var type in GetValues.GetTypes(modelGraph))
            {
                if (!MatchesModelId(type, ckModelId)) continue;
                await AddTitle(outputFile, null, type.CkTypeId.Key.SemanticVersionedFullName);
                await AddHierarchy(outputFile, type, baseRelativePath);
                await TextWrapper.AddDescription(outputFile, "SAMPLE DESCRIPTION");

                if (type.DefinedAttributes.Count != 0)
                {
                    await outputFile.WriteLineAsync(
                        $"| {Text.ID} | {Text.AutoCompleteValues} | {Text.AutoIncrementReference} | {Text.IsOptional} |");
                    await outputFile.WriteLineAsync("| -----------| -----------| -----------| ----------- |");

                    foreach (var attribute in type.DefinedAttributes)
                    {
                        await attribute.DrawAttribute(outputFile, baseRelativePath, linkHelpers);
                    }
                }

                //For Drawing all Associations that are associated with type
                if (type.Associations.DefinedAssociations.Count == 0) continue;
                var tableBuilt = false;

                foreach (var association in type.Associations.Out.Owned)
                {
                    foreach (var item in GetValues.GetAssociationRoles(modelGraph))
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
                            baseRelativePath, linkHelpers);
                    }
                }
            }
        }
        else
        {
            logger.LogInformation("No Types to draw for model ID: {ckModelId}", ckModelId);
        }
    }

    public async Task GenerateAssociationRolesMarkdownTable(ILogger<Command<OctoToolOptions>> logger, CkModelGraph modelGraph,
        string documentPath, CkModelId ckModelId)
    {
        
        // Check if there are any association roles to draw before proceeding
        var associationRoles = GetValues.GetAssociationRoles(modelGraph)
            .Where(associationRole => MatchesModelId(associationRole, ckModelId));
        var ckAssociationRoleGraphs = associationRoles as CkAssociationRoleGraph[] ?? associationRoles.ToArray();
        var baseRelativePath = directoryTools.GetRelativeDestinationDirectory(documentPath);
        
        if (ckAssociationRoleGraphs.Length != 0)
        {
            directoryTools.BuildDirectory(documentPath, ckModelId);
            await using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Associations"));

            await outputFile.WriteLineAsync(
                $"| {Text.ID} | {Text.InboundMultiplicity} | {Text.InboundName} |" +
                $" {Text.OutboundMultiplicity}| {Text.OutboundName}| {Text.TargetCKTypeID}| {Text.TargetAttributes}|");
            await outputFile.WriteLineAsync("| -----------| -----------| -----------| -----------|" +
                                            " -----------| -----------| ----------- |");

            foreach (var associationRole in ckAssociationRoleGraphs)
            {
                await associationRole.DrawAssociationRole(outputFile, null, baseRelativePath, linkHelpers);
            }
        }
        else
        {
            logger.LogInformation("No association roles to draw for model ID: {ckModelId}", ckModelId);
        }
    }

    public async Task GenerateVersionHistory(string docPath, CkModelId ckModelId)
    {
        directoryTools.BuildDirectory(docPath, ckModelId);
        await using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(docPath, ckModelId, "VersionHistory"));

        await outputFile.WriteLineAsync($"| {Text.Version} | {Text.Description} |");
        await outputFile.WriteLineAsync("| -----------| -----------|");
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
            var builder = new LinkItemBuilder(obj, baseRelativePath, linkHelpers);
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
}