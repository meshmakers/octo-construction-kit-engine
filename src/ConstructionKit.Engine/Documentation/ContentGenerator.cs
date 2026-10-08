using Meshmakers.Common.Shared;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

internal class ContentGenerator(
    ILogger<ContentGenerator> logger,
    IDirectoryTools directoryTools,
    ILinkHelpers linkHelpers)
    : IContentGenerator
{
    private readonly InheritanceHelpers _inheritanceHelpers = new(linkHelpers);

    public async Task GenerateAttributesMarkdownTable(CkModelGraph modelGraph, string documentPath, CkModelId ckModelId,
        string? versionNumber, string linkPathRoot)
    {
        var attributes = modelGraph.GetAttributes().Where(attribute => MatchesModelId(attribute, ckModelId));

        if (attributes.Any())
        {
            directoryTools.BuildDirectory(documentPath, ckModelId);

#if NETSTANDARD2_0
            using StreamWriter outputFile =
                new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Attributes"));
#else
            await using StreamWriter outputFile =
 new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Attributes"));
#endif

            //Newline to fix acorn issue in Docusaurus
            await outputFile.WriteLineAsync().ConfigureAwait(false);

            if (versionNumber != null)
            {
                await AddVersionInfo(outputFile, versionNumber).ConfigureAwait(false);
            }

            await DrawAttributeTables(modelGraph, ckModelId, linkPathRoot, outputFile).ConfigureAwait(false);
        }
        else
        {
            logger.LogDebug("No Attributes to draw for model ID: {CkModelId}", ckModelId);
        }
    }

    private async Task DrawAttributeTables(CkModelGraph modelGraph, CkModelId ckModelId, string directoryPath,
        StreamWriter outputFile)
    {
        foreach (var attribute in modelGraph.GetAttributes().OrderBy(a => a.CkAttributeId.ElementId))
        {
            //Checks for If the Attributes Model ID is the Same as the one that was given
            if (!MatchesModelId(attribute, ckModelId))
            {
                continue;
            }

            await AddTitle(outputFile, null, attribute.CkAttributeId.ElementId.SemanticVersionedFullName)
                .ConfigureAwait(false);
            await WriteCkV2Modifiers(outputFile, attribute.Visibility).ConfigureAwait(false);

            if (attribute.Description != null)
            {
                await TextWrapper.AddDescription(outputFile, attribute.Description).ConfigureAwait(false);
            }

            await outputFile.WriteLineAsync("##### Data Type").ConfigureAwait(false);

            switch (attribute.ValueType)
            {
                case AttributeValueTypesDto.Record:
                case AttributeValueTypesDto.Enum:
                    await outputFile
                        .WriteLineAsync(
                            $"{attribute.ValueType.ToString().ToConstantCase()}: {attribute.LinkToRecordOrEnum(directoryPath, linkHelpers)}")
                        .ConfigureAwait(false);
                    break;
                case AttributeValueTypesDto.RecordArray:
                    await outputFile
                        .WriteLineAsync(
                            $"Array of {attribute.ValueType.ToString().ToConstantCase()}: {attribute.LinkToRecordOrEnum(directoryPath, linkHelpers)}")
                        .ConfigureAwait(false);
                    break;
                default:
                    await outputFile.WriteLineAsync(attribute.ValueType.ToString().ToConstantCase())
                        .ConfigureAwait(false);
                    break;
            }

            await outputFile.WriteLineAsync().ConfigureAwait(false);

            if (attribute.DefaultValues != null)
            {
                await outputFile.WriteLineAsync("##### Default Values").ConfigureAwait(false);
                await outputFile.WriteLineAsync("| Default Value |").ConfigureAwait(false);
                await outputFile.WriteLineAsync("| ----------- |").ConfigureAwait(false);
                foreach (var value in attribute.DefaultValues)
                {
                    var escapedValue = TextWrapper.EscapeMdxSpecialCharacters(value?.ToString());
                    await outputFile.WriteLineAsync($"| {escapedValue} |").ConfigureAwait(false);
                }
            }

            if (attribute.MetaData?.Any() ?? false)
            {
                await outputFile.WriteLineAsync("##### Meta Data").ConfigureAwait(false);
                await outputFile.WriteLineAsync("| Key | Value |").ConfigureAwait(false);
                await outputFile.WriteLineAsync("| ----------- | ----------- |").ConfigureAwait(false);
                foreach (var metaData in attribute.MetaData)
                {
                    var escapedKey = TextWrapper.EscapeMdxSpecialCharacters(metaData.Key);
                    var escapedMetaValue = TextWrapper.EscapeMdxSpecialCharacters(metaData.Value);
                    await outputFile.WriteLineAsync($"| {escapedKey} | {escapedMetaValue} |").ConfigureAwait(false);
                }
            }

            await outputFile.WriteLineAsync("---").ConfigureAwait(false);
        }
    }

    public async Task GenerateEnumsMarkdownTable(CkModelGraph modelGraph, string documentPath, CkModelId ckModelId,
        string? versionNumber,
        string linkPathRoot)
    {
        var enums = modelGraph.GetEnums().Where(en => MatchesModelId(en, ckModelId));
        if (enums.Any())
        {
            directoryTools.BuildDirectory(documentPath, ckModelId);

#if NETSTANDARD2_0
            using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Enums"));
#else
            await using StreamWriter outputFile =
 new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Enums"));
#endif

            //Newline to fix acorn issue in Docusaurus
            await outputFile.WriteLineAsync().ConfigureAwait(false);

            if (versionNumber != null)
            {
                await AddVersionInfo(outputFile, versionNumber).ConfigureAwait(false);
            }

            await DrawEnumTables(modelGraph, ckModelId, outputFile).ConfigureAwait(false);
        }
        else
        {
            logger.LogDebug("No Enums to draw for model ID: {ckModelId}", ckModelId);
        }
    }

    private static async Task DrawEnumTables(CkModelGraph modelGraph, CkModelId ckModelId, StreamWriter outputFile)
    {
        foreach (var @enum in modelGraph.GetEnums())
        {
            if (!MatchesModelId(@enum, ckModelId)) continue;
            await AddTitle(outputFile, null, @enum.CkEnumId.ElementId.SemanticVersionedFullName).ConfigureAwait(false);
            await WriteCkV2Modifiers(outputFile, @enum.Visibility).ConfigureAwait(false);

            if (@enum.Description != null)
            {
                await TextWrapper.AddDescription(outputFile, @enum.Description).ConfigureAwait(false);
            }

            await outputFile.WriteLineAsync($"|  {Text.ID} | {Text.Values} | {Text.Descriptions} |")
                .ConfigureAwait(false);
            await outputFile.WriteLineAsync("| -----------| -----------| -----------|").ConfigureAwait(false);

            await @enum.DrawEnum(outputFile).ConfigureAwait(false);
        }
    }

    public async Task GenerateRecordsMarkdownTable(CkModelGraph modelGraph, string documentPath, CkModelId ckModelId,
        string? versionNumber,
        string linkPathRoot)
    {
        var records = modelGraph.GetRecords().Where(record => MatchesModelId(record, ckModelId));

        if (records.Any())
        {
            directoryTools.BuildDirectory(documentPath, ckModelId);

#if NETSTANDARD2_0
            using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Records"));
#else
            await using StreamWriter outputFile =
 new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Records"));
#endif

            //Newline to fix acorn issue in Docusaurus
            await outputFile.WriteLineAsync().ConfigureAwait(false);

            if (versionNumber != null)
            {
                await AddVersionInfo(outputFile, versionNumber).ConfigureAwait(false);
            }

            await DrawRecordTables(modelGraph, ckModelId, linkPathRoot, outputFile).ConfigureAwait(false);
        }
        else
        {
            logger.LogDebug("No Records to draw for model ID: {ckModelId}", ckModelId);
        }
    }

    private async Task DrawRecordTables(CkModelGraph modelGraph, CkModelId ckModelId, string directoryPath,
        StreamWriter outputFile)
    {
        foreach (var record in modelGraph.GetRecords())
        {
            if (!MatchesModelId(record, ckModelId)) continue;

            await AddTitle(outputFile, null, record.CkRecordId.ElementId.SemanticVersionedFullName).ConfigureAwait(false);
            await WriteCkV2Modifiers(outputFile, record.Visibility, record.Derivable).ConfigureAwait(false);

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

    public async Task GenerateTypesMarkdownTable(CkModelGraph modelGraph, string documentPath, CkModelId ckModelId,
        string? versionNumber,
        string linkPathRoot)
    {
        var typeGraphs = modelGraph.GetTypes().Where(typeGraph => MatchesModelId(typeGraph, ckModelId));

        if (typeGraphs.Any())
        {
            directoryTools.BuildDirectory(documentPath, ckModelId);

#if NETSTANDARD2_0
            using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Types"));
#else
            await using StreamWriter outputFile =
 new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Types"));
#endif

            //Newline to fix acorn issue in Docusaurus
            await outputFile.WriteLineAsync().ConfigureAwait(false);

            if (versionNumber != null)
            {
                await AddVersionInfo(outputFile, versionNumber).ConfigureAwait(false);
            }

            await DrawTypeEntry(modelGraph, ckModelId, linkPathRoot, outputFile).ConfigureAwait(false);
        }
        else
        {
            logger.LogDebug("No Types to draw for model ID: {ckModelId}", ckModelId);
        }
    }

    private async Task DrawTypeEntry(CkModelGraph modelGraph, CkModelId ckModelId, string directoryPath,
        StreamWriter outputFile)
    {
        foreach (var type in modelGraph.GetTypes())
        {
            if (!MatchesModelId(type, ckModelId))
            {
                continue;
            }

            await AddTitle(outputFile, null, type.CkTypeId.ElementId.SemanticVersionedFullName).ConfigureAwait(false);
            await WriteCkV2Modifiers(outputFile, type.Visibility, type.Derivable).ConfigureAwait(false);
            await _inheritanceHelpers.AddHierarchy(outputFile, type, directoryPath).ConfigureAwait(false);
            if (type.Description != null)
            {
                await TextWrapper.AddDescription(outputFile, type.Description).ConfigureAwait(false);
            }
            else
            {
                await outputFile.WriteLineAsync("No description available currently.").ConfigureAwait(false);
            }

            await DrawTypeTables(directoryPath, type, outputFile).ConfigureAwait(false);

            await GenerateTypeAssociationsTable(type, outputFile, directoryPath).ConfigureAwait(false);

            await GenerateCkV2TypeSections(type, outputFile).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     CK v2 (F1.1-S4, AB#5907): writes the effective <c>visibility</c> / <c>derivable</c> of an element, only
    ///     when they differ from the v1 defaults (<c>Public</c> / <c>Any</c>), so ckLanguage 1 documentation is
    ///     unchanged.
    /// </summary>
    private static async Task WriteCkV2Modifiers(StreamWriter outputFile, CkVisibilityDto visibility,
        CkDerivableDto derivable = CkDerivableDto.Any)
    {
        if (visibility != CkVisibilityDto.Public)
        {
            await outputFile.WriteLineAsync($"Visibility: `{visibility}`").ConfigureAwait(false);
            await outputFile.WriteLineAsync().ConfigureAwait(false);
        }

        if (derivable != CkDerivableDto.Any)
        {
            await outputFile.WriteLineAsync($"Derivable: `{derivable}`").ConfigureAwait(false);
            await outputFile.WriteLineAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     CK v2 (AB#5667 / AB#5669): implemented interfaces and methods of a type. Emitted only when the type has
    ///     any, so the documentation of a ckLanguage 1 model is unchanged.
    /// </summary>
    private static async Task GenerateCkV2TypeSections(CkTypeGraph type, StreamWriter outputFile)
    {
        if (type.AllImplementedInterfaces.Count != 0)
        {
            await outputFile.WriteLineAsync().ConfigureAwait(false);
            await outputFile.WriteLineAsync("Implements: " + string.Join(", ",
                    type.AllImplementedInterfaces.Select(i => $"`{i.ToRtCkId().FullName}`").OrderBy(i => i)))
                .ConfigureAwait(false);
        }

        if (type.AllMethods.Count != 0)
        {
            await outputFile.WriteLineAsync().ConfigureAwait(false);
            await outputFile.WriteLineAsync("| Method | Kind | Parameters | Result | Declared by |").ConfigureAwait(false);
            await outputFile.WriteLineAsync("| ----------- | ----------- | ----------- | ----------- | ----------- |")
                .ConfigureAwait(false);
            foreach (var method in type.AllMethods.Values.OrderBy(m => m.Definition.MethodId))
            {
                var parameters = string.Join(", ", (method.Definition.Parameters ?? []).Select(p =>
                    $"{p.Name}{(p.IsOptional ? "?" : "")}: {p.ValueType}"));
                await outputFile.WriteLineAsync(
                        $"| {method.Definition.MethodId}{(method.Visibility == CkVisibilityDto.Internal ? " (internal)" : "")} | {method.Definition.Kind} | {parameters} | " +
                        $"{method.Definition.Result?.ValueType.ToString() ?? "-"} | {method.DeclaringCkTypeId.ToRtCkId().SemanticVersionedFullName} |")
                    .ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     CK v2 (AB#5667): one table per interface of the model with its members.
    /// </summary>
    public async Task GenerateInterfacesMarkdownTable(CkModelGraph modelGraph, string documentPath, CkModelId ckModelId,
        string? versionNumber)
    {
        var interfaces = modelGraph.Interfaces.Values.Where(i => i.CkInterfaceId.ModelId == ckModelId).ToList();
        if (interfaces.Count == 0)
        {
            logger.LogDebug("No Interfaces to draw for model ID: {ckModelId}", ckModelId);
            return;
        }

        directoryTools.BuildDirectory(documentPath, ckModelId);
#if NETSTANDARD2_0
        using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Interfaces"));
#else
        await using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Interfaces"));
#endif
        await outputFile.WriteLineAsync().ConfigureAwait(false);
        if (versionNumber != null)
        {
            await AddVersionInfo(outputFile, versionNumber).ConfigureAwait(false);
        }

        foreach (var ckInterface in interfaces.OrderBy(i => i.CkInterfaceId))
        {
            await AddTitle(outputFile, null, ckInterface.CkInterfaceId.ElementId.FullName).ConfigureAwait(false);
            await WriteCkV2Modifiers(outputFile, ckInterface.Visibility).ConfigureAwait(false);
            if (ckInterface.Deprecated)
            {
                await outputFile.WriteLineAsync("**Deprecated.**").ConfigureAwait(false);
                await outputFile.WriteLineAsync().ConfigureAwait(false);
            }

            await outputFile.WriteLineAsync(ckInterface.Description ?? "No description available currently.")
                .ConfigureAwait(false);
            await outputFile.WriteLineAsync().ConfigureAwait(false);
            if (ckInterface.DeclaredExtends.Count != 0)
            {
                await outputFile.WriteLineAsync("Extends: " + string.Join(", ",
                        ckInterface.DeclaredExtends.Select(i => $"`{i.ToRtCkId().FullName}`")))
                    .ConfigureAwait(false);
                await outputFile.WriteLineAsync().ConfigureAwait(false);
            }

            // F1.1-S5: AllAttributes includes the members inherited through extends.
            if (ckInterface.AllAttributes.Count != 0)
            {
                await outputFile.WriteLineAsync("| Member | Is optional | Value type | CK attribute ID |").ConfigureAwait(false);
                await outputFile.WriteLineAsync("| ----------- | ----------- | ----------- | ----------- |").ConfigureAwait(false);
                foreach (var member in ckInterface.AllAttributes.Values.OrderBy(a => a.AttributeName))
                {
                    await outputFile.WriteLineAsync(
                            $"| {member.AttributeName} | {member.IsOptional} | {member.ValueType} | {member.CkAttributeId.ToRtCkId().FullName} |")
                        .ConfigureAwait(false);
                }

                await outputFile.WriteLineAsync().ConfigureAwait(false);
            }

            if (ckInterface.AllAssociations.Count != 0)
            {
                await outputFile.WriteLineAsync("| Association | Target | Multiplicity | Is optional | Declared by |")
                    .ConfigureAwait(false);
                await outputFile.WriteLineAsync("| ----------- | ----------- | ----------- | ----------- | ----------- |")
                    .ConfigureAwait(false);
                foreach (var association in ckInterface.AllAssociations)
                {
                    var definition = association.Definition;
                    var target = definition.TargetCkTypeId?.ToRtCkId().FullName ??
                                 definition.TargetCkInterfaceId?.ToRtCkId().FullName;
                    await outputFile.WriteLineAsync(
                            $"| {definition.CkRoleId.ToRtCkId().FullName} | {target} | " +
                            $"{definition.Multiplicity?.ToString() ?? "any"} | {definition.IsOptional} | " +
                            $"{association.DeclaringCkInterfaceId.ToRtCkId().FullName} |")
                        .ConfigureAwait(false);
                }

                await outputFile.WriteLineAsync().ConfigureAwait(false);
            }

            if (ckInterface.AllMethods.Count != 0)
            {
                await outputFile.WriteLineAsync("| Method | Kind | Parameters | Result | Declared by |").ConfigureAwait(false);
                await outputFile.WriteLineAsync("| ----------- | ----------- | ----------- | ----------- | ----------- |")
                    .ConfigureAwait(false);
                foreach (var method in ckInterface.AllMethods.Values.OrderBy(m => m.Definition.MethodId))
                {
                    var parameters = string.Join(", ", (method.Definition.Parameters ?? []).Select(p =>
                        $"{p.Name}{(p.IsOptional ? "?" : "")}: {p.ValueType}"));
                    await outputFile.WriteLineAsync(
                            $"| {method.Definition.MethodId}{(method.Visibility == CkVisibilityDto.Internal ? " (internal)" : "")} | " +
                            $"{method.Definition.Kind} | {parameters} | {method.Definition.Result?.ValueType.ToString() ?? "-"} | " +
                            $"{method.DeclaringCkInterfaceId.ToRtCkId().FullName} |")
                        .ConfigureAwait(false);
                }

                await outputFile.WriteLineAsync().ConfigureAwait(false);
            }

            if (ckInterface.ImplementingTypes.Count != 0)
            {
                await outputFile.WriteLineAsync("Implemented by: " + string.Join(", ",
                        ckInterface.ImplementingTypes.Select(t => $"`{t.ToRtCkId().SemanticVersionedFullName}`").OrderBy(t => t)))
                    .ConfigureAwait(false);
                await outputFile.WriteLineAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task DrawTypeTables(string directoryPath, CkTypeGraph type, StreamWriter outputFile)
    {
        if (type.DefinedAttributes.Count != 0)
        {
            await outputFile.WriteLineAsync(
                    $"| {Text.ID} | {Text.AutoCompleteValues} | {Text.AutoIncrementReference} |" +
                    $" {Text.IsOptional} |")
                .ConfigureAwait(false);
            await outputFile.WriteLineAsync("| -----------| -----------| -----------| ----------- |")
                .ConfigureAwait(false);

            foreach (var attribute in type.DefinedAttributes)
            {
                await attribute.DrawAttribute(outputFile, directoryPath, linkHelpers).ConfigureAwait(false);
            }
        }
    }

    private async Task GenerateTypeAssociationsTable(CkTypeGraph type, StreamWriter outputFile, string baseRelativePath)
    {
        if (!type.Associations.DefinedAssociations.Any())
        {
            return;
        }

        if (type.Associations.Out.Owned.Any())
        {
            await AddTitle(outputFile, null, "Outbound Associations",
                    true)
                .ConfigureAwait(false);

            await outputFile.WriteLineAsync(
                    $"| {Text.OutboundName} | {Text.OutboundMultiplicity} | {Text.RoleId} |" +
                    $" {Text.TargetCKTypeID} | {Text.TargetAttributes}|")
                .ConfigureAwait(false);
            await outputFile.WriteLineAsync("| -----------| -----------| -----------| -----------|" +
                                            " -----------|")
                .ConfigureAwait(false);

            foreach (var association in type.Associations.Out.Owned)
            {
                await association.DrawTypeAssociation(GraphDirections.Outbound, outputFile,
                        baseRelativePath, linkHelpers)
                    .ConfigureAwait(false);
            }

            await outputFile.WriteLineAsync("").ConfigureAwait(false);
        }

        if (type.Associations.In.Owned.Any())
        {
            await AddTitle(outputFile, null, "Inbound Associations",
                    true)
                .ConfigureAwait(false);

            await outputFile.WriteLineAsync(
                    $"| {Text.InboundName} | {Text.InboundMultiplicity} | {Text.RoleId} |" +
                    $" {Text.TargetCKTypeID} | {Text.TargetAttributes}|")
                .ConfigureAwait(false);
            await outputFile.WriteLineAsync("| -----------| -----------| -----------| -----------|" +
                                            " -----------|")
                .ConfigureAwait(false);

            foreach (var association in type.Associations.In.Owned)
            {
                await association.DrawTypeAssociation(GraphDirections.Inbound, outputFile,
                        baseRelativePath, linkHelpers)
                    .ConfigureAwait(false);
            }

            await outputFile.WriteLineAsync("").ConfigureAwait(false);
        }
    }

    public async Task GenerateAssociationRolesMarkdownTable(CkModelGraph modelGraph, string documentPath,
        CkModelId ckModelId,
        string? versionNumber, string linkPathRoot)
    {
        // Check if there are any association roles to draw before proceeding
        var associationRoles = modelGraph.GetAssociationRoles()
            .Where(associationRole => MatchesModelId(associationRole, ckModelId));
        var ckAssociationRoleGraphs = associationRoles as CkAssociationRoleGraph[] ?? associationRoles.ToArray();

        if (ckAssociationRoleGraphs.Length != 0)
        {
            directoryTools.BuildDirectory(documentPath, ckModelId);
#if NETSTANDARD2_0
            using StreamWriter outputFile =
                new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Associations"));
#else
            await using StreamWriter outputFile =
 new(linkHelpers.GetGeneratedFilePath(documentPath, ckModelId, "Associations"));
#endif

            //Newline to fix acorn issue in Docusaurus
            await outputFile.WriteLineAsync().ConfigureAwait(false);

            if (versionNumber != null)
            {
                await AddVersionInfo(outputFile, versionNumber).ConfigureAwait(false);
            }

            foreach (var associationRole in ckAssociationRoleGraphs.OrderBy(a => a.CkRoleId.ElementId))
            {
                await AddTitle(outputFile, null, associationRole.CkRoleId.ElementId.SemanticVersionedFullName)
                    .ConfigureAwait(false);
                await WriteCkV2Modifiers(outputFile, associationRole.Visibility).ConfigureAwait(false);

                if (associationRole.Description != null)
                {
                    await TextWrapper.AddDescription(outputFile, associationRole.Description).ConfigureAwait(false);
                }
                else
                {
                    await outputFile.WriteLineAsync("No description available currently.").ConfigureAwait(false);
                }

                await associationRole.DrawAssociationRole(outputFile, directoryTools, linkHelpers)
                    .ConfigureAwait(false);
            }
        }
        else
        {
            logger.LogDebug("No association roles to draw for model ID: {ckModelId}", ckModelId);
        }
    }

    public async Task GenerateVersionHistory(string docPath, CkModelId ckModelId, string? versionNumber,
        string linkPathRoot)
    {
        directoryTools.BuildDirectory(docPath, ckModelId);
#if NETSTANDARD2_0
        using StreamWriter outputFile = new(linkHelpers.GetGeneratedFilePath(docPath, ckModelId, "VersionHistory"));
#else
        await using StreamWriter outputFile =
 new(linkHelpers.GetGeneratedFilePath(docPath, ckModelId, "VersionHistory"));
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
            CkAttributeGraph attribute => attribute.CkAttributeId.ModelId.SemanticVersionedFullName ==
                                          modelId.SemanticVersionedFullName,
            CkEnumGraph enumGraph => enumGraph.CkEnumId.ModelId.SemanticVersionedFullName ==
                                     modelId.SemanticVersionedFullName,
            CkRecordGraph recordGraph => recordGraph.CkRecordId.ModelId.SemanticVersionedFullName ==
                                         modelId.SemanticVersionedFullName,
            CkTypeGraph ckTypeGraph => ckTypeGraph.CkTypeId.ModelId.SemanticVersionedFullName ==
                                       modelId.SemanticVersionedFullName,
            CkAssociationRoleGraph ckAssociationRoleGraph => ckAssociationRoleGraph.CkRoleId.ModelId
                                                                 .SemanticVersionedFullName ==
                                                             modelId.SemanticVersionedFullName,
            _ => false // Handle unsupported types or throw an exception if needed
        };
    }

    private static async Task AddTitle(StreamWriter outputFile, CkModelId? ckModelId, string tableTitle,
        bool isSubtitle = false)
    {
        string titlePrefix;

        if (isSubtitle)
        {
            titlePrefix = "# ";
        }
        else if (ckModelId != null)
        {
            titlePrefix = $" {ckModelId.Name} ";
        }
        else
        {
            titlePrefix = " ";
        }

        await outputFile.WriteLineAsync($"###{titlePrefix}{tableTitle}").ConfigureAwait(false);
    }

    internal static async Task AddVersionInfo(StreamWriter outputFile, string versionNumber)
    {
        await outputFile.WriteLineAsync($"<span class=\"badge badge--secondary\">Version: {versionNumber}</span>")
            .ConfigureAwait(false);
        await outputFile.WriteLineAsync("<p></p>").ConfigureAwait(false);
    }
}