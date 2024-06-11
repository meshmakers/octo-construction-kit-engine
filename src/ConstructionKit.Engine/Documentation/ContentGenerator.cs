using System.Text;
using Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations.GenerateDocsTools;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

internal class ContentGenerator(ILogger<ContentGenerator> logger, IDirectoryTools directoryTools,
    ILinkHelpers linkHelpers) : IContentGenerator
{

    public async Task GenerateAttributesMarkdownTable(CkModelGraph modelGraph, string 
        documentPath, CkModelId ckModelId, string? versionNumber)
    {
        var attributes = GetValues.GetAttributes(modelGraph)
            .Where(attribute => MatchesModelId(attribute, ckModelId));
        var baseRelativePath = directoryTools.GetRelativeDestinationDirectory(documentPath);
        
        if (attributes.Any())
        {
            directoryTools.BuildDirectory(documentPath, ckModelId);

            using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Attributes"));

            if (versionNumber != null)
            {
                await AddVersionInfo(outputFile, versionNumber).ConfigureAwait(false);
            }
            
            await AddVersionInfo(outputFile, "3").ConfigureAwait(false);
            
            await outputFile.WriteLineAsync(
                $"| {Text.ID} | {Text.DataType} | {Text.DefaultValues} | {Text.IsDataStream} |" +
                $" {Text.Description} | {Text.CkEnumId_CkRecordId} |").ConfigureAwait(false);
            await outputFile.WriteLineAsync("| -----------| -----------| -----------| -----------| -----------|" +
                                            " ----------- |").ConfigureAwait(false);
            

            //Checks for If the Attributes Model ID is the Same as the one that was given
            foreach (var attribute in GetValues.GetAttributes(modelGraph))
            {
                if (MatchesModelId(attribute, ckModelId))
                {
                    await attribute.DrawAttribute(outputFile, baseRelativePath, linkHelpers).ConfigureAwait(false);
                }
            }
        }
        else
        {
            logger.LogDebug("No Attributes to draw for model ID: {ckModelId}", ckModelId);
        }
    }

    public async Task GenerateEnumsMarkdownTable(CkModelGraph modelGraph, string documentPath, 
        CkModelId ckModelId, string? versionNumber)
    {
        var enums = GetValues.GetEnums(modelGraph)
            .Where(en => MatchesModelId(en, ckModelId));
        if (enums.Any())
        {
            directoryTools.BuildDirectory(documentPath, ckModelId);

            using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Enums"));

            if (versionNumber != null)
            {
                await AddVersionInfo(outputFile, versionNumber).ConfigureAwait(false);
            }

            foreach (var @enum in GetValues.GetEnums(modelGraph))
            {
                if (!MatchesModelId(@enum, ckModelId)) continue;
                await AddTitle(outputFile, null, @enum.CkEnumId.Key.SemanticVersionedFullName).ConfigureAwait(false);

                await outputFile.WriteLineAsync($"|  {Text.ID} | {Text.Values} | {Text.Descriptions} |").ConfigureAwait(false);
                await outputFile.WriteLineAsync("| -----------| -----------| -----------|").ConfigureAwait(false);
                    
                await @enum.DrawEnum(outputFile).ConfigureAwait(false);
            }
        }
        else
        {
            logger.LogDebug("No Enums to draw for model ID: {ckModelId}", ckModelId);
        }
    }

    public async Task GenerateRecordsMarkdownTable(CkModelGraph modelGraph, string documentPath, 
        CkModelId ckModelId, string? versionNumber)
    {
        var records = GetValues.GetRecords(modelGraph)
            .Where(record => MatchesModelId(record, ckModelId));

        if (records.Any())
        {
            directoryTools.BuildDirectory(documentPath, ckModelId);

            using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Records"));
            
            if (versionNumber != null)
            {
                await AddVersionInfo(outputFile, versionNumber).ConfigureAwait(false);
            }
            
            await outputFile.WriteLineAsync(
                $"| {Text.ID}| {Text.DefinedAttributes} | {Text.IsOptional} | {Text.AutoIncrementReference} |" +
                $" {Text.AutoCompleteValues} | {Text.CKAttributeID} |").ConfigureAwait(false);
            await outputFile.WriteLineAsync("| -----------| -----------| -----------| -----------| -----------|" +
                                            " ----------- |").ConfigureAwait(false);

            foreach (var record in GetValues.GetRecords(modelGraph))
            {
                if (MatchesModelId(record, ckModelId))
                {
                    await record.DrawRecord(outputFile).ConfigureAwait(false);
                }
            }
        }
        else
        {
            logger.LogDebug("No Records to draw for model ID: {ckModelId}", ckModelId);
        }
    }

    public async Task GenerateTypesMarkdownTable(CkModelGraph modelGraph, 
        string documentPath, CkModelId ckModelId, string? versionNumber)
    {
        var typeGraphs = GetValues.GetTypes(modelGraph)
            .Where(typeGraph => MatchesModelId(typeGraph, ckModelId));
        var baseRelativePath = directoryTools.GetRelativeDestinationDirectory(documentPath);
        
        if (typeGraphs.Any())
        {
            directoryTools.BuildDirectory(documentPath, ckModelId);

            using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Types"));
            
            if (versionNumber != null)
            {
                await AddVersionInfo(outputFile, versionNumber).ConfigureAwait(false);
            }

            foreach (var type in GetValues.GetTypes(modelGraph))
            {
                if (!MatchesModelId(type, ckModelId)) continue;
                await AddTitle(outputFile, null, type.CkTypeId.Key.SemanticVersionedFullName).ConfigureAwait(false);
                await AddHierarchy(outputFile, type, baseRelativePath).ConfigureAwait(false);
                await TextWrapper.AddDescription(outputFile, "SAMPLE DESCRIPTION").ConfigureAwait(false);

                if (type.DefinedAttributes.Count != 0)
                {
                    await outputFile.WriteLineAsync(
                        $"| {Text.ID} | {Text.AutoCompleteValues} | {Text.AutoIncrementReference} |" +
                        $" {Text.IsOptional} |").ConfigureAwait(false);
                    await outputFile.WriteLineAsync("| -----------| -----------| -----------| ----------- |").ConfigureAwait(false);

                    foreach (var attribute in type.DefinedAttributes)
                    {
                        await attribute.DrawAttribute(outputFile, baseRelativePath, linkHelpers).ConfigureAwait(false);
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
                                true).ConfigureAwait(false);

                            await outputFile.WriteLineAsync(
                                $"| {Text.ID} | {Text.InboundMultiplicity} | {Text.InboundName} |" +
                                $" {Text.OutboundMultiplicity}| {Text.OutboundName}| {Text.TargetCKTypeID}|" +
                                $" {Text.TargetAttributes}|").ConfigureAwait(false);
                            await outputFile.WriteLineAsync("| -----------| -----------| -----------| -----------|" +
                                                            " -----------| -----------| ----------- |").ConfigureAwait(false);
                            tableBuilt = true;
                        }

                        await item.DrawAssociationRole(outputFile, association,
                            baseRelativePath, linkHelpers).ConfigureAwait(false);
                    }
                }
            }
        }
        else
        {
            logger.LogDebug("No Types to draw for model ID: {ckModelId}", ckModelId);
        }
    }

    public async Task GenerateAssociationRolesMarkdownTable(CkModelGraph modelGraph,
        string documentPath, CkModelId ckModelId, string? versionNumber)
    {
        
        // Check if there are any association roles to draw before proceeding
        var associationRoles = GetValues.GetAssociationRoles(modelGraph)
            .Where(associationRole => MatchesModelId(associationRole, ckModelId));
        var ckAssociationRoleGraphs = associationRoles as CkAssociationRoleGraph[] ?? associationRoles.ToArray();
        var baseRelativePath = directoryTools.GetRelativeDestinationDirectory(documentPath);
        
        if (ckAssociationRoleGraphs.Length != 0)
        {
            directoryTools.BuildDirectory(documentPath, ckModelId);
            using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Associations"));

            if (versionNumber != null)
            {
                await AddVersionInfo(outputFile, versionNumber).ConfigureAwait(false);
            }
            
            await outputFile.WriteLineAsync(
                $"| {Text.ID} | {Text.InboundMultiplicity} | {Text.InboundName} |" +
                $" {Text.OutboundMultiplicity}| {Text.OutboundName}|" +
                $" {Text.TargetCKTypeID}| {Text.TargetAttributes}|").ConfigureAwait(false);
            await outputFile.WriteLineAsync("| -----------| -----------| -----------| -----------|" +
                                            " -----------| -----------| ----------- |").ConfigureAwait(false);

            foreach (var associationRole in ckAssociationRoleGraphs)
            {
                await associationRole.DrawAssociationRole(outputFile, null, baseRelativePath, linkHelpers).ConfigureAwait(false);
            }
        }
        else
        {
            logger.LogDebug("No association roles to draw for model ID: {ckModelId}", ckModelId);
        }
    }

    public async Task GenerateVersionHistory(string docPath, CkModelId ckModelId, string? versionNumber)
    {
        directoryTools.BuildDirectory(docPath, ckModelId);
        using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(docPath, ckModelId, "VersionHistory"));

        if (versionNumber != null)
        {
            await AddVersionInfo(outputFile, versionNumber).ConfigureAwait(false);
        }
        
        await outputFile.WriteLineAsync($"| {Text.Version} | {Text.Description} |").ConfigureAwait(false);
        await outputFile.WriteLineAsync("| -----------| -----------|").ConfigureAwait(false);
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

        await outputFile.WriteLineAsync($"###{titlePrefix}{tableTitle}").ConfigureAwait(false);
    }

    private async Task AddHierarchy(StreamWriter outputFile, CkTypeGraph ckTypeGraph, string baseRelativePath)
    {
        var hierarchy = ReconstructHierarchyFromPath(ckTypeGraph.Path, baseRelativePath);
        await outputFile.WriteLineAsync($"**Inheritance:** {hierarchy}").ConfigureAwait(false);
    }

    private string ReconstructHierarchyFromPath(string path, string baseRelativePath)
    {
        string[] separators = ["->", ":"];

        var parts = path.Split(separators, StringSplitOptions.None).Select(s => s.Trim()).ToArray();

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

    private static async Task AddVersionInfo(StreamWriter outputFile, string versionNumber)
    {
        await outputFile.WriteLineAsync($"#### Version: [{versionNumber}](https://docs.meshmakers.cloud)").ConfigureAwait(false);
    }
}