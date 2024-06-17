using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

internal class ContentGenerator(ILogger<ContentGenerator> logger, IDirectoryTools directoryTools, ILinkHelpers linkHelpers)
    : IContentGenerator
{
    private readonly InheritanceHelpers _inheritanceHelpers = new(linkHelpers);

    public async Task GenerateAttributesMarkdownTable(CkModelGraph modelGraph, string documentPath, CkModelId ckModelId,
        string? versionNumber, string directoryPath)
    {
        var attributes = GetValues.GetAttributes(modelGraph).Where(attribute => MatchesModelId(attribute, ckModelId));

        if (attributes.Any())
        {
            directoryTools.BuildDirectory(documentPath, ckModelId);

#if NETSTANDARD2_0
            using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Attributes"));
#else
            await using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Attributes"));
#endif

            //Newline to fix acorn issue in Docusaurus
            await outputFile.WriteLineAsync().ConfigureAwait(false);

            if (versionNumber != null)
            {
                await AddVersionInfo(outputFile, versionNumber).ConfigureAwait(false);
            }
            

            //Checks for If the Attributes Model ID is the Same as the one that was given
            foreach (var attribute in GetValues.GetAttributes(modelGraph))
            {
                if (!MatchesModelId(attribute, ckModelId)) continue;
                
                if (attribute.Description != null)
                {
                    await TextWrapper.AddDescription(outputFile, attribute.Description).ConfigureAwait(false);
                }
                
                await outputFile.WriteLineAsync(
                        $"| {Text.ID} | {Text.DataType} | {Text.DefaultValues} | {Text.IsDataStream} |" +
                        $" {Text.Description} | {Text.CkEnumId_CkRecordId} |")
                    .ConfigureAwait(false);
                await outputFile.WriteLineAsync("| -----------| -----------| -----------| -----------| -----------| ----------- |")
                    .ConfigureAwait(false);
                    
                await attribute.DrawAttribute(outputFile, directoryPath, linkHelpers).ConfigureAwait(false);
            }
        }
        else
        {
            logger.LogDebug("No Attributes to draw for model ID: {ckModelId}", ckModelId);
        }
    }

    public async Task GenerateEnumsMarkdownTable(CkModelGraph modelGraph, string documentPath, CkModelId ckModelId, string? versionNumber,
        string directoryPath)
    {
        var enums = GetValues.GetEnums(modelGraph).Where(en => MatchesModelId(en, ckModelId));
        if (enums.Any())
        {
            directoryTools.BuildDirectory(documentPath, ckModelId);

#if NETSTANDARD2_0
            using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Enums"));
#else
            await using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Enums"));
#endif

            //Newline to fix acorn issue in Docusaurus
            await outputFile.WriteLineAsync().ConfigureAwait(false);

            if (versionNumber != null)
            {
                await AddVersionInfo(outputFile, versionNumber).ConfigureAwait(false);
            }

            foreach (var @enum in GetValues.GetEnums(modelGraph))
            {
                if (!MatchesModelId(@enum, ckModelId)) continue;
                await AddTitle(outputFile, null, @enum.CkEnumId.Key.SemanticVersionedFullName).ConfigureAwait(false);

                if (@enum.Description != null)
                {
                    await TextWrapper.AddDescription(outputFile, @enum.Description).ConfigureAwait(false);
                }
                
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

    public async Task GenerateRecordsMarkdownTable(CkModelGraph modelGraph, string documentPath, CkModelId ckModelId, string? versionNumber,
        string directoryPath)
    {
        var records = GetValues.GetRecords(modelGraph).Where(record => MatchesModelId(record, ckModelId));

        if (records.Any())
        {
            directoryTools.BuildDirectory(documentPath, ckModelId);

#if NETSTANDARD2_0
            using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Records"));
#else
            await using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Records"));
#endif

            //Newline to fix acorn issue in Docusaurus
            await outputFile.WriteLineAsync().ConfigureAwait(false);

            if (versionNumber != null)
            {
                await AddVersionInfo(outputFile, versionNumber).ConfigureAwait(false);
            }

            foreach (var record in GetValues.GetRecords(modelGraph))
            {
                if (!MatchesModelId(record, ckModelId)) continue;

                await AddTitle(outputFile, null, record.CkRecordId.Key.SemanticVersionedFullName).ConfigureAwait(false);

                await _inheritanceHelpers.AddRecordHierarchy(outputFile, record, directoryPath).ConfigureAwait(false);

                if (record.Description != null)
                {
                    await TextWrapper.AddDescription(outputFile, record.Description).ConfigureAwait(false);
                }
                
                await outputFile.WriteLineAsync(
                        $"| {Text.DefinedAttributes} | {Text.IsOptional} | {Text.AutoIncrementReference} |" +
                        $" {Text.AutoCompleteValues} | {Text.CKAttributeID} |")
                    .ConfigureAwait(false);
                await outputFile.WriteLineAsync("|  -----------| -----------| -----------| -----------| ----------- |")
                    .ConfigureAwait(false);

                await record.DrawRecord(outputFile).ConfigureAwait(false);
            }
        }
        else
        {
            logger.LogDebug("No Records to draw for model ID: {ckModelId}", ckModelId);
        }
    }

    public async Task GenerateTypesMarkdownTable(CkModelGraph modelGraph, string documentPath, CkModelId ckModelId, string? versionNumber,
        string directoryPath)
    {
        var typeGraphs = GetValues.GetTypes(modelGraph).Where(typeGraph => MatchesModelId(typeGraph, ckModelId));

        if (typeGraphs.Any())
        {
            directoryTools.BuildDirectory(documentPath, ckModelId);

#if NETSTANDARD2_0
            using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Types"));
#else
            await using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Types"));
#endif

            //Newline to fix acorn issue in Docusaurus
            await outputFile.WriteLineAsync().ConfigureAwait(false);

            if (versionNumber != null)
            {
                await AddVersionInfo(outputFile, versionNumber).ConfigureAwait(false);
            }

            foreach (var type in GetValues.GetTypes(modelGraph))
            {
                if (!MatchesModelId(type, ckModelId)) continue;
                await AddTitle(outputFile, null, type.CkTypeId.Key.SemanticVersionedFullName).ConfigureAwait(false);
                await _inheritanceHelpers.AddHierarchy(outputFile, type, directoryPath).ConfigureAwait(false);
                if (type.Description != null)
                {
                    await TextWrapper.AddDescription(outputFile, type.Description).ConfigureAwait(false);
                }
                
                await DrawTypeTables(directoryPath, type, outputFile).ConfigureAwait(false);

                await GenerateTypesAttributesTable(modelGraph, directoryPath, type, outputFile).ConfigureAwait(false);
            }
        }
        else
        {
            logger.LogDebug("No Types to draw for model ID: {ckModelId}", ckModelId);
        }
    }

    private async Task DrawTypeTables(string directoryPath, CkTypeGraph type, StreamWriter outputFile)
    {
        if (type.DefinedAttributes.Count != 0)
        {
            await outputFile.WriteLineAsync(
                    $"| {Text.ID} | {Text.AutoCompleteValues} | {Text.AutoIncrementReference} |" + $" {Text.IsOptional} |")
                .ConfigureAwait(false);
            await outputFile.WriteLineAsync("| -----------| -----------| -----------| ----------- |").ConfigureAwait(false);

            foreach (var attribute in type.DefinedAttributes)
            {
                await attribute.DrawAttribute(outputFile, directoryPath, linkHelpers).ConfigureAwait(false);
            }
        }
    }

    private async Task GenerateTypesAttributesTable(CkModelGraph modelGraph, string directoryPath, CkTypeGraph type,
        StreamWriter outputFile)
    {
        //For Drawing all Associations that are associated with type
        if (type.Associations.DefinedAssociations.Count == 0) return;
        var tableBuilt = false;

        foreach (var association in type.Associations.Out.Owned)
        {
            foreach (var item in GetValues.GetAssociationRoles(modelGraph))
            {
                if (association.CkRoleId != item.CkRoleId) continue;
                if (!tableBuilt)
                {
                    await AddTitle(outputFile, null, type.CkTypeId.Key.SemanticVersionedFullName + " Associations", true)
                        .ConfigureAwait(false);

                    await outputFile.WriteLineAsync(
                            $"| {Text.ID} | {Text.InboundMultiplicity} | {Text.InboundName} |" +
                            $" {Text.OutboundMultiplicity}| {Text.OutboundName}| {Text.TargetCKTypeID}|" + $" {Text.TargetAttributes}|")
                        .ConfigureAwait(false);
                    await outputFile.WriteLineAsync("| -----------| -----------| -----------| -----------|" +
                                                    " -----------| -----------| ----------- |")
                        .ConfigureAwait(false);
                    tableBuilt = true;
                }

                await item.DrawAssociationRole(outputFile, association, directoryPath, linkHelpers).ConfigureAwait(false);
            }
        }
    }

    public async Task GenerateAssociationRolesMarkdownTable(CkModelGraph modelGraph, string documentPath, CkModelId ckModelId,
        string? versionNumber, string directoryPath)
    {
        // Check if there are any association roles to draw before proceeding
        var associationRoles = GetValues.GetAssociationRoles(modelGraph)
            .Where(associationRole => MatchesModelId(associationRole, ckModelId));
        var ckAssociationRoleGraphs = associationRoles as CkAssociationRoleGraph[] ?? associationRoles.ToArray();

        if (ckAssociationRoleGraphs.Length != 0)
        {
            directoryTools.BuildDirectory(documentPath, ckModelId);
#if NETSTANDARD2_0
            using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Associations"));
#else
            await using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Associations"));
#endif

            //Newline to fix acorn issue in Docusaurus
            await outputFile.WriteLineAsync().ConfigureAwait(false);

            if (versionNumber != null)
            {
                await AddVersionInfo(outputFile, versionNumber).ConfigureAwait(false);
            }

            foreach (var associationRole in ckAssociationRoleGraphs)
            {
                if (associationRole.Description != null)
                {
                    await TextWrapper.AddDescription(outputFile, associationRole.Description).ConfigureAwait(false);
                }
                
                await outputFile.WriteLineAsync(
                        $"| {Text.ID} | {Text.InboundMultiplicity} | {Text.InboundName} |" +
                        $" {Text.OutboundMultiplicity}| {Text.OutboundName}|" +
                        $" {Text.TargetCKTypeID}| {Text.TargetAttributes}|")
                    .ConfigureAwait(false);
                await outputFile.WriteLineAsync("| -----------| -----------| -----------| -----------|" +
                                                " -----------| -----------| ----------- |")
                    .ConfigureAwait(false);
                
                await associationRole.DrawAssociationRole(outputFile, null, directoryPath, linkHelpers).ConfigureAwait(false);
            }
        }
        else
        {
            logger.LogDebug("No association roles to draw for model ID: {ckModelId}", ckModelId);
        }
    }

    public async Task GenerateVersionHistory(string docPath, CkModelId ckModelId, string? versionNumber, string directoryPath)
    {
        directoryTools.BuildDirectory(docPath, ckModelId);
#if NETSTANDARD2_0
        using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(docPath, ckModelId, "VersionHistory"));
#else
        await using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(docPath, ckModelId, "VersionHistory"));
#endif

        //Newline to fix acorn issue in Docusaurus
        await outputFile.WriteLineAsync().ConfigureAwait(false);

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

    internal static async Task AddVersionInfo(StreamWriter outputFile, string versionNumber)
    {
        await outputFile.WriteLineAsync($"<span class=\"badge badge--secondary\">Version: {versionNumber}</span>").ConfigureAwait(false);
        await outputFile.WriteLineAsync("<p></p>").ConfigureAwait(false);
    }
}